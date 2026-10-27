import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { Api } from '../../core/api';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { accessWorkspace, accessUser, accessLine, accessGrantEvidence, copyableInvitation } from './access-contracts';
import { RoleAssignmentDialog, AccessRevocationDialog, roleScopeDescription } from './roles';
import { MicrosoftDirectory } from './directory';
import { AccessHistory } from './access-history';
import { tenantWorkspace } from './tenant-contracts';
import { TenantProvisioningDialog } from './provisioning';
import { ManagedGroupsDialog } from './groups';
import { TenantOperationRecoveryDialog } from './operation-recovery';
import { capabilityAvailable, recoverableOperation } from './operations-contracts';

@Component({ selector: 'audit-user-access', imports: [ReactiveFormsModule, MatButtonModule, MicrosoftDirectory, AccessHistory, ...SHARED], styles: [`
  .user-card { display: grid; gap: .75rem; }
  .user-head { display: flex; flex-wrap: wrap; align-items: center; gap: .75rem; }
  .user-avatar { display: grid; place-items: center; flex: none; width: 2.5rem; height: 2.5rem; border-radius: 50%;
    background: var(--as-accent-ink, #1e4fa8); color: #fff; font-weight: 700; }
  .user-name { flex: 1 1 12rem; min-width: 0; }
  .user-name h2 { margin: 0; }
  .user-name p { margin: .125rem 0 0; color: var(--as-text-muted, #52667a); overflow-wrap: anywhere; }
  .user-facts dd { min-width: 0; overflow-wrap: anywhere; }
  .user-card h3 { margin: .25rem 0 0; }
  .grant-list { margin: 0; padding: 0; list-style: none; border-top: 1px solid var(--as-line, #dfe6ee); }
  .grant { display: grid; grid-template-columns: minmax(0, 1fr) auto; align-items: center; gap: .25rem 1rem; padding: .5rem 0;
    border-bottom: 1px solid var(--as-line, #dfe6ee); }
  .grant-text { display: grid; gap: .125rem; min-width: 0; }
  .grant-text span { color: var(--as-text-muted, #52667a); font-size: .875rem; overflow-wrap: anywhere; }
  .grant-actions, .user-actions { display: flex; flex-wrap: wrap; gap: .25rem .5rem; }
  .grant-history { grid-column: 1 / -1; margin: .25rem 0; padding: .5rem .75rem .5rem 1.75rem; border-radius: var(--as-radius-sm, 6px);
    background: var(--as-surface-sunken, #f6f8fb); font-size: .875rem; overflow-wrap: anywhere; }
  .grant-history .history-head { list-style: none; margin-inline-start: -1rem; font-weight: 600; }
  @media (max-width: 560px) { .grant { grid-template-columns: 1fr; } }
`], template: `
  <audit-page-header title="Users & Access" eyebrow="Firm administration" description="Local AuditSphere roles and scope are separate from Microsoft directory authority." />
  <button matButton (click)="ws.reload()">Refresh access</button>
  <audit-state [loading]="ws.loading()" [error]="ws.error()" label="firm user access" />
  @if (ws.data(); as w) {
    <form [formGroup]="filter" (ngSubmit)="search()"><label for="access-search">Search local users</label><input id="access-search" formControlName="query" maxlength="100" />
      <label><input type="checkbox" formControlName="disabled" /> Disabled or revoked users</label><button matButton="filled" type="submit">Search local access</button></form>
    <div class="actions"><button matButton (click)="page.set(page() - 1)" [disabled]="page() === 0">Previous users</button><span>Page {{ page() + 1 }}</span>
      <button matButton (click)="page.set(page() + 1)" [disabled]="(page() + 1) * 25 >= filtered().length">Next users</button></div>
    @for(u of visible(); track u.userId) {
      <section class="panel user-card">
        <div class="user-head">
          <span class="user-avatar" aria-hidden="true">{{ (u.displayName || '?').slice(0, 1).toUpperCase() }}</span>
          <div class="user-name"><h2>{{ u.displayName }}</h2><p>{{ u.microsoftIdentity }}</p></div>
          <audit-status [value]="u.auditSphereStatus" />
        </div>
        <dl class="facts user-facts">
          <dt>Account type</dt><dd>{{ u.accountType }}</dd>
          <dt>Directory status</dt><dd>{{ u.directoryStatus | codeLabel }}</dd>
          <dt>Invitation</dt><dd>{{ u.invitationStatus | codeLabel }}</dd>
          <dt>Microsoft verification</dt><dd>{{ u.lastMicrosoftVerification ? (u.lastMicrosoftVerification | utcTime) : 'Not observed' }}</dd>
          <dt>Last application access</dt><dd>{{ u.lastAuditSphereAccess ? (u.lastAuditSphereAccess | utcTime) : 'Not observed' }}</dd>
          <dt>Created</dt><dd>{{ u.createdDate | utcTime }} · {{ u.createdBy }}</dd>
          <dt>Tenant ID</dt><dd><code>{{ u.tenantId }}</code></dd>
          <dt>Object ID</dt><dd><code>{{ u.objectId }}</code></dd>
        </dl>
        <h3>AuditSphere roles and scope</h3>
        <ul class="grant-list">
          @for (g of u.access; track g.grantId) {
            <li class="grant">
              <div class="grant-text"><strong>{{ g.role | codeLabel }}</strong>
                <span>{{ scopeLabel(g, w) }} · {{ g.expiresAt ? 'Expires ' + (g.expiresAt | utcTime) : 'No expiry' }}</span></div>
              <div class="grant-actions">
                <button matButton (click)="toggleHistory(g.grantId)" [attr.aria-expanded]="expandedGrant() === g.grantId" [attr.aria-label]="'View history for ' + g.role + ' for ' + u.displayName">History</button>
                <button matButton (click)="revoke(u, g)" [attr.aria-label]="'Revoke ' + g.role + ' for ' + u.displayName">Revoke</button>
              </div>
              @if (expandedGrant() === g.grantId) {
                <ul class="grant-history"><li class="history-head">Grant change evidence (latest 20)</li>
                  @for (e of grantHistory().get(g.grantId) ?? []; track e.at) {
                    <li><time [attr.datetime]="e.at">{{ e.at | utcTime }}</time> · {{ e.action | codeLabel }} ·
                      {{ e.priorRole ? (e.priorRole | codeLabel) : 'None' }} → {{ e.newRole ? (e.newRole | codeLabel) : 'None' }} ·
                      {{ e.reason ?? (e.source | codeLabel) }} · by {{ e.actor }}</li>
                  } @empty { <li>No recorded changes for this grant.</li> }
                </ul>
              }
            </li>
          } @empty { <li class="grant">No active grants</li> }
        </ul>
        <div class="user-actions">
          <button matButton="filled" (click)="assign(u)" [disabled]="u.auditSphereStatus === 'DISABLED'">Assign role and scope</button>
          @if (u.invitationId) { <button matButton (click)="copyInvitation(u)" [disabled]="copying()">Copy invitation link</button> }
        </div>
      </section>
    } @empty { <p>No local users match this view.</p> }
    @if (inviteLink()) { <p>Invitation link: <code>{{ inviteLink() }}</code></p> }
    <audit-command-message [message]="inviteMessage()" [failed]="inviteFailed()" />
    <section class="panel"><h2>Optional Microsoft administration</h2><p>Existing guests can be selected in Microsoft directory below. New invitations and new workforce accounts require separately enabled, verified consent. No client contact triggers a B2B invitation automatically.</p>
      <button matButton="filled" (click)="provision(false)" [disabled]="!available('TENANT_USER_PROVISIONING')">Create Microsoft 365 user</button>
      <button matButton="filled" (click)="provision(true)" [disabled]="!available('GUEST_INVITATION')">Invite new guest</button>
      <button matButton (click)="groups()" [disabled]="!available('GROUP_MEMBERSHIP')">Manage approved Microsoft groups</button>
      <audit-state [loading]="settings.loading()" [error]="settings.error()" label="optional Microsoft capabilities" />
      @for(c of settings.data()?.workspace?.capabilities ?? []; track c.capability){ @if(['TENANT_USER_PROVISIONING','GUEST_INVITATION','GROUP_MEMBERSHIP'].includes(c.capability)){ <p>{{ c.displayName }} · {{ c.permission }} · {{ c.state }} · {{ c.diagnosticCode }}</p> } }
    </section>
    <section class="panel"><h2>Microsoft operation history</h2><p>An unknown outcome requires reconciliation before the effect is repeated.</p>
      @for(o of w.operations; track o.id) { <details><summary>{{ o.kind }} · {{ o.state }} · {{ o.target }}</summary><p>{{ o.reason }} · {{ o.resultCode }} · {{ o.reconciliation }}</p><p>Operation {{ o.id }} · correlation {{ o.correlationId }} · actor {{ o.requestedBy }} · {{ o.updatedAt }}</p>
        @if(recoverable(o.state,o.kind)){ <button matButton (click)="recover(o.id)">Review operation recovery</button> }
      </details> }
      @empty { <p>No external administration operations recorded.</p> }
    </section>
    <section class="panel"><h2>Bind a roster identity</h2><p>Directory-reader fallback: bind an exact verified identity from the approved roster or a verified sign-in, then assign its role and scope below. This never creates or changes a Microsoft account.</p>
      <form [formGroup]="roster" (ngSubmit)="bindRoster()">
        <label for="roster-tenant">Tenant ID</label><input id="roster-tenant" formControlName="tenantId" maxlength="200" required />
        <label for="roster-subject">Object ID / subject</label><input id="roster-subject" formControlName="subject" maxlength="200" required />
        <label for="roster-email">Email</label><input id="roster-email" type="email" formControlName="email" maxlength="320" required />
        <label for="roster-name">Display name</label><input id="roster-name" formControlName="displayName" maxlength="300" required />
        <label for="roster-kind">Application identity kind</label><select id="roster-kind" formControlName="userKind"><option value="Staff">Staff</option><option value="Client">Client</option></select>
        <label for="roster-source">Identity evidence</label><select id="roster-source" formControlName="source"><option value="APPROVED_ROSTER">Approved roster</option><option value="VERIFIED_SIGN_IN">Verified sign-in</option></select>
        <button matButton="filled" type="submit" [disabled]="roster.invalid || rosterBusy() || rosterUncertain()">Bind exact identity</button>
      </form>
      <audit-command-message [message]="rosterMessage()" [failed]="rosterFailed()" />
    </section>
    <audit-access-history [rows]="w.history" />
  }
  <audit-microsoft-directory [parentAccess]="ws.data()" (changed)="ws.reload()" />
` })
export class UserAccess {
  private readonly api = inject(Api); private readonly dialog = inject(MatDialog);
  readonly ws = this.api.resource(() => '/api/ui/administration/access', accessWorkspace, 'Current firm-wide Administrator access is required.');
  readonly settings = this.api.resource(() => '/api/ui/administration/microsoft365', tenantWorkspace);
  readonly recoverable = recoverableOperation;
  readonly filter = inject(FormBuilder).nonNullable.group({ query: '', disabled: false });
  readonly applied = signal({ query: '', disabled: false }); readonly page = signal(0);
  readonly filtered = computed(() => { const w = this.ws.data(); const f = this.applied(); return (f.disabled ? w?.disabledOrRevoked : w?.users)?.filter(u =>
    (u.displayName + ' ' + u.microsoftIdentity + ' ' + u.objectId).toLowerCase().includes(f.query.toLowerCase())) ?? []; });
  readonly visible = computed(() => this.filtered().slice(this.page() * 25, this.page() * 25 + 25));
  constructor() { effect(() => { if (!this.ws.data()) { this.filter.reset(); this.applied.set({ query: '', disabled: false }); this.page.set(0); } }); }
  search(): void { this.applied.set(this.filter.getRawValue()); this.page.set(0); }
  assign(user: ReturnType<typeof accessUser>): void { const workspace = this.ws.data(); if (!workspace) return;
    this.dialog.open(RoleAssignmentDialog, { data: { user, workspace }, width: '48rem', maxWidth: '96vw', autoFocus: 'first-heading' }).afterClosed().subscribe(changed => { if(changed) this.ws.reload(); }); }
  revoke(user: ReturnType<typeof accessUser>, grant: ReturnType<typeof accessLine>): void {
    this.dialog.open(AccessRevocationDialog, { data: { displayName: user.displayName, microsoftIdentity: user.microsoftIdentity, grant }, width: '35rem', maxWidth: '96vw', autoFocus: 'first-heading' }).afterClosed().subscribe(changed => { if(changed) this.ws.reload(); }); }
  available(capability:string):boolean{return !!this.settings.data()?.configuredTenantId && capabilityAvailable(this.settings.data()?.workspace.capabilities ?? [],capability);}
  provision(invite:boolean):void{const workspace=this.ws.data(),tenantId=this.settings.data()?.configuredTenantId;if(!workspace||!tenantId||!this.available(invite?'GUEST_INVITATION':'TENANT_USER_PROVISIONING'))return;
    this.dialog.open(TenantProvisioningDialog,{data:{workspace,tenantId,invite},width:'46rem',maxWidth:'96vw',autoFocus:'first-heading'}).afterClosed().subscribe(changed=>{if(changed)this.ws.reload();});}
  groups():void{const workspace=this.ws.data();if(!workspace||!this.available('GROUP_MEMBERSHIP'))return;
    this.dialog.open(ManagedGroupsDialog,{data:workspace,width:'46rem',maxWidth:'96vw',autoFocus:'first-heading'}).afterClosed().subscribe(changed=>{if(changed)this.ws.reload();});}
  recover(id:string):void{if(!this.ws.data()?.operations.some(o=>o.id===id))return;
    this.dialog.open(TenantOperationRecoveryDialog,{data:id,width:'42rem',maxWidth:'96vw',autoFocus:'first-heading'}).afterClosed().subscribe(changed=>{if(changed)this.ws.reload();});}
  /** Readable grant scope: named scopes drop the appended identifier and unnamed ones show it shortened; the role dialog keeps exact values. */
  scopeLabel(grant: ReturnType<typeof accessLine>, workspace: ReturnType<typeof accessWorkspace>): string {
    const guid = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}';
    return roleScopeDescription(grant, workspace).replace(new RegExp(` \\(${guid}\\)`, 'gi'), '')
      .replace(new RegExp(guid, 'gi'), id => id.slice(0, 8) + '…');
  }
  readonly expandedGrant = signal<string | null>(null);
  toggleHistory(grantId: string): void { this.expandedGrant.set(this.expandedGrant() === grantId ? null : grantId); }
  readonly grantHistory = computed(() => { const map = new Map<string, ReturnType<typeof accessGrantEvidence>[]>();
    for (const e of this.ws.data()?.grantHistory ?? []) { const list = map.get(e.grantId) ?? []; list.push(e); map.set(e.grantId, list); } return map; });
  readonly copying = signal(false); readonly inviteLink = signal(''); readonly inviteMessage = signal(''); readonly inviteFailed = signal(false);
  private readonly session = inject(SessionService);
  async copyInvitation(user: ReturnType<typeof accessUser>): Promise<void> {
    if (!user.invitationId || this.copying()) return;
    const generation = this.session.invalidation(); this.copying.set(true); this.inviteMessage.set(''); this.inviteFailed.set(false);
    try {
      const invitation = await this.api.get('/api/ui/administration/access/invitations/' + user.invitationId, copyableInvitation);
      if (generation !== this.session.invalidation()) return;
      if (navigator.clipboard?.writeText) await navigator.clipboard.writeText(invitation.link);
      else { this.inviteLink.set(invitation.link); this.inviteFailed.set(true);
        this.inviteMessage.set('Copy is unavailable in this browser; copy the link shown below and share it through the approved channel.'); return; }
      const r = await this.api.command('/api/ui/administration/access/invitations/' + user.invitationId + '/copied');
      if (generation !== this.session.invalidation()) return;
      this.inviteMessage.set(r.ok ? 'Copied and recorded.' : r.message); this.inviteFailed.set(!r.ok);
      if (r.ok) this.ws.reload();
    } catch (e) {
      if (generation !== this.session.invalidation()) return;
      this.inviteFailed.set(true); this.inviteMessage.set(e instanceof Error ? e.message : 'The invitation link could not be loaded. Refresh and try again.');
      this.ws.reload();
    } finally { this.copying.set(false); }
  }
  readonly roster = inject(FormBuilder).nonNullable.group({
    tenantId: ['', Validators.required], subject: ['', Validators.required], email: ['', [Validators.required, Validators.email]],
    displayName: ['', Validators.required], userKind: 'Staff', source: 'APPROVED_ROSTER' });
  readonly rosterBusy = signal(false); readonly rosterUncertain = signal(false); readonly rosterMessage = signal(''); readonly rosterFailed = signal(false);
  async bindRoster(): Promise<void> {
    if (this.roster.invalid || this.rosterBusy() || this.rosterUncertain()) return;
    const generation = this.session.invalidation(); this.rosterBusy.set(true); this.rosterMessage.set(''); this.rosterFailed.set(false);
    try {
      const r = await this.api.command<{ userId: string }>('/api/ui/administration/access/bind-manual', this.roster.getRawValue());
      if (generation !== this.session.invalidation()) return;
      if (r.ok) { this.rosterMessage.set('Exact identity bound. Review and assign its explicit role and scope from the user list before it can access anything.');
        this.roster.reset({ tenantId: '', subject: '', email: '', displayName: '', userKind: 'Staff', source: 'APPROVED_ROSTER' }); this.ws.reload(); }
      else { this.rosterFailed.set(true); this.rosterMessage.set(r.message); if (r.unknown) this.rosterUncertain.set(true); }
    } finally { this.rosterBusy.set(false); }
  }
}
