import { Component, computed, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { Api } from '../../core/api';
import { SHARED } from '../../core/ui';
import { accessWorkspace, accessUser, accessLine } from './access-contracts';
import { RoleAssignmentDialog, AccessRevocationDialog } from './roles';
import { MicrosoftDirectory } from './directory';

@Component({ selector: 'audit-user-access', imports: [ReactiveFormsModule, MatButtonModule, MicrosoftDirectory, ...SHARED], template: `
  <audit-page-header title="Users & Access" eyebrow="Firm administration" description="Local AuditSphere roles and scope are separate from Microsoft directory authority." />
  <button matButton (click)="ws.reload()">Refresh access</button>
  <audit-state [loading]="ws.loading()" [error]="ws.error()" label="firm user access" />
  @if (ws.data(); as w) {
    <form [formGroup]="filter" (ngSubmit)="search()"><label for="access-search">Search local users</label><input id="access-search" formControlName="query" maxlength="100" />
      <label><input type="checkbox" formControlName="disabled" /> Disabled or revoked users</label><button matButton="filled" type="submit">Search local access</button></form>
    <div class="actions"><button matButton (click)="page.set(page() - 1)" [disabled]="page() === 0">Previous users</button><span>Page {{ page() + 1 }}</span>
      <button matButton (click)="page.set(page() + 1)" [disabled]="(page() + 1) * 25 >= filtered().length">Next users</button></div>
    @for(u of visible(); track u.userId) {
      <section class="panel"><h2>{{ u.displayName }}</h2><p>{{ u.microsoftIdentity }}</p><audit-status [value]="u.auditSphereStatus" />
        <dl class="facts"><dt>Tenant ID</dt><dd>{{ u.tenantId }}</dd><dt>Object ID</dt><dd>{{ u.objectId }}</dd><dt>Account type</dt><dd>{{ u.accountType }}</dd>
          <dt>Directory status</dt><dd>{{ u.directoryStatus }}</dd><dt>Invitation</dt><dd>{{ u.invitationStatus }}</dd><dt>Microsoft verification</dt><dd>{{ u.lastMicrosoftVerification ?? 'Not observed' }}</dd>
          <dt>Last application access</dt><dd>{{ u.lastAuditSphereAccess ?? 'Not observed' }}</dd><dt>Created by</dt><dd>{{ u.createdBy }}</dd><dt>Created</dt><dd>{{ u.createdDate }}</dd></dl>
        <h3>AuditSphere roles and scope</h3><ul>@for(g of u.access; track g.grantId) { <li>{{ g.role }} · {{ g.scopeKind }} · {{ g.engagementId ?? g.clientId ?? g.groupId ?? 'Firm' }} · expires {{ g.expiresAt ?? 'No expiry' }}
          <button matButton (click)="revoke(g)" [attr.aria-label]="'Revoke ' + g.role + ' for ' + u.displayName">Revoke</button></li> } @empty { <li>No active grants</li> }</ul>
        <button matButton="filled" (click)="assign(u)" [disabled]="u.auditSphereStatus === 'DISABLED'">Assign role and scope</button>
      </section>
    } @empty { <p>No local users match this view.</p> }
    <audit-microsoft-directory />
    <section class="panel"><h2>Microsoft operation history</h2><p>An unknown outcome requires reconciliation before the effect is repeated.</p>
      @for(o of w.operations; track o.id) { <details><summary>{{ o.kind }} · {{ o.state }} · {{ o.target }}</summary><p>{{ o.reason }} · {{ o.resultCode }} · {{ o.reconciliation }}</p><p>Operation {{ o.id }} · correlation {{ o.correlationId }} · actor {{ o.requestedBy }} · {{ o.updatedAt }}</p></details> }
      @empty { <p>No external administration operations recorded.</p> }
    </section>
    <section class="panel"><h2>Access history</h2><ul>@for(h of w.history; track $index) { <li>{{ h.at }} · {{ h.operation }} · {{ h.target }} · {{ h.change }} · {{ h.reason }} · {{ h.actor }} · {{ h.result }}</li> } @empty { <li>No access changes recorded.</li> }</ul></section>
  }
` })
export class UserAccess {
  private readonly api = inject(Api); private readonly dialog = inject(MatDialog);
  readonly ws = this.api.resource(() => '/api/ui/administration/access', accessWorkspace, 'Current firm-wide Administrator access is required.');
  readonly filter = inject(FormBuilder).nonNullable.group({ query: '', disabled: false });
  readonly applied = signal({ query: '', disabled: false }); readonly page = signal(0);
  readonly filtered = computed(() => { const w = this.ws.data(); const f = this.applied(); return (f.disabled ? w?.disabledOrRevoked : w?.users)?.filter(u =>
    (u.displayName + ' ' + u.microsoftIdentity + ' ' + u.objectId).toLowerCase().includes(f.query.toLowerCase())) ?? []; });
  readonly visible = computed(() => this.filtered().slice(this.page() * 25, this.page() * 25 + 25));
  constructor() { effect(() => { if (!this.ws.data()) { this.filter.reset(); this.applied.set({ query: '', disabled: false }); this.page.set(0); } }); }
  search(): void { this.applied.set(this.filter.getRawValue()); this.page.set(0); }
  assign(user: ReturnType<typeof accessUser>): void { const workspace = this.ws.data(); if (!workspace) return;
    this.dialog.open(RoleAssignmentDialog, { data: { user, workspace }, width: '48rem', maxWidth: '96vw', autoFocus: 'first-heading' }).afterClosed().subscribe(changed => { if(changed) this.ws.reload(); }); }
  revoke(grant: ReturnType<typeof accessLine>): void {
    this.dialog.open(AccessRevocationDialog, { data: grant, width: '35rem', maxWidth: '96vw', autoFocus: 'first-heading' }).afterClosed().subscribe(changed => { if(changed) this.ws.reload(); }); }
}
