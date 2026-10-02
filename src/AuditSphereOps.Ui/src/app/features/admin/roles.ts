import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { arr, decode, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { accessWorkspace, accessLine, accessUser, roleReview } from './access-contracts';

type Workspace = ReturnType<typeof accessWorkspace>;
type User = ReturnType<typeof accessUser>;
type RoleDialogData = { user: User; workspace: Workspace };
/** Presentation hint only; the Application service independently validates identity kind and scope. */
export function roleChoices(accountType: string, catalogue: string[]): { roles: string[]; scopes: string[]; initialRole: string } {
  return accountType.startsWith('Client (')
    ? { roles: catalogue.filter(r => r === 'ClientUser'), scopes: ['CLIENT', 'ENGAGEMENT'], initialRole: 'ClientUser' }
    : { roles: catalogue.filter(r => r !== 'ClientUser'), scopes: ['FIRM_WIDE', 'CLIENT', 'ENGAGEMENT', 'GROUP'], initialRole: 'Staff' };
}
@Component({ selector: 'audit-role-assignment', imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, ...SHARED], template: `
  <h2 mat-dialog-title>Assign AuditSphere access</h2><mat-dialog-content>
    <p>{{ data.user.displayName }} · {{ data.user.microsoftIdentity }}</p><p>Local roles never assign Microsoft Entra administrator roles or Microsoft group membership.</p>
    <form [formGroup]="form" (ngSubmit)="preview()">
      <label for="local-role">Role</label><select id="local-role" formControlName="role">@for (r of choices().roles; track r) { <option [value]="r">{{ r }}</option> }</select>
      <label for="scope-kind">Scope type</label><select id="scope-kind" formControlName="scopeKind">@for (scope of choices().scopes; track scope) { <option [value]="scope">{{ scope }}</option> }</select>
      @if (form.controls.scopeKind.value === 'CLIENT' || form.controls.scopeKind.value === 'ENGAGEMENT') {
        <label for="role-client">Client</label><select id="role-client" formControlName="clientId"><option value="">Select client</option>@for(c of data.workspace.clients; track c.id) { <option [value]="c.id">{{ c.name }}</option> }</select>
      }
      @if (form.controls.scopeKind.value === 'ENGAGEMENT') {
        <label for="role-engagement">Engagement</label><select id="role-engagement" formControlName="engagementId"><option value="">Select engagement</option>@for(e of data.workspace.engagements; track e.id) { @if(e.parentId === form.controls.clientId.value) { <option [value]="e.id">{{ e.name }}</option> } }</select>
      }
      @if (form.controls.scopeKind.value === 'GROUP') {
        <label for="role-group">Client group</label><select id="role-group" formControlName="groupId"><option value="">Select group</option>@for(g of data.workspace.clientGroups; track g.id) { <option [value]="g.id">{{ g.name }}</option> }</select>
      }
      <label for="role-replaces">Replace existing grant (optional)</label><select id="role-replaces" formControlName="replacesGrantId"><option value="">Add separate grant</option>@for(g of data.user.access; track g.grantId) { @if(!g.groupGrant) { <option [value]="g.grantId">{{ g.role }} · {{ g.scopeKind }}</option> } }</select>
      <label for="role-effective">Effective from (local time, optional)</label><input id="role-effective" type="datetime-local" formControlName="effectiveFrom" />
      <label for="role-expiry">Expires at (local time, optional)</label><input id="role-expiry" type="datetime-local" formControlName="expiresAt" />
      <label for="role-reason">Reason</label><textarea id="role-reason" formControlName="reason" maxlength="1000"></textarea>
      @if (form.controls.reason.touched && form.controls.reason.invalid) { <p role="alert">Record a reason of at least five characters.</p> }
      <button matButton="filled" [disabled]="busy() || uncertain() || form.invalid">Review proposed access</button>
    </form>
    <audit-state [loading]="catalogue.loading()" [error]="catalogue.error()" label="the role catalogue" />
    @if (review(); as p) {
      <h3>Current access</h3><ul>@for(g of p.review.currentAccess; track g.grantId) { <li>{{ g.role }} · {{ g.scopeKind }} · {{ g.clientId ?? g.groupId ?? 'Firm' }} · expires {{ g.expiresAt ?? 'No expiry' }}</li> } @empty { <li>No current access</li> }</ul>
      <h3>Proposed access</h3><p>{{ p.review.proposed.role }} · {{ p.review.proposed.scopeKind }} · {{ p.review.proposed.clientId ?? p.review.proposed.groupId ?? 'Firm' }} · expires {{ p.review.proposed.expiresAt ?? 'No expiry' }}</p>
      <h3>Added capabilities</h3><ul>@for(c of p.review.addedCapabilities; track c) { <li>{{ c }}</li> } @empty { <li>None</li> }</ul>
      <h3>Removed capabilities</h3><ul>@for(c of p.review.removedCapabilities; track c) { <li>{{ c }}</li> } @empty { <li>None</li> }</ul>
      <p>Scope expansion: {{ p.review.scopeExpansion ? 'Yes' : 'No' }} · scope reduction: {{ p.review.scopeReduction ? 'Yes' : 'No' }}</p>
      <h3>Professional independence impact</h3><ul>@for(i of p.review.independenceImpact; track i) { <li>{{ i }}</li> } @empty { <li>No recorded impact</li> }</ul>
      <ul>@for(w of p.review.warnings; track w) { <li>{{ w }}</li> }</ul>
      @if (p.review.blockingReasons.length) { <ul role="alert">@for(b of p.review.blockingReasons; track b) { <li>{{ b }}</li> }</ul> }
      <label><input type="checkbox" [formControl]="confirmed" /> I reviewed the proposed access, scope change, expiry and independence impact.</label>
      <button matButton="filled" (click)="save()" [disabled]="busy() || uncertain() || !confirmed.value || !!p.review.blockingReasons.length">Assign reviewed access</button>
    }
    <audit-command-message [message]="message()" [failed]="failed()" />
  </mat-dialog-content><mat-dialog-actions><button matButton (click)="dialog.close()" [disabled]="busy()">Close</button></mat-dialog-actions>
`, styles: `form { display: grid; gap: .5rem; } label { margin-top: .5rem; } textarea { min-height: 5rem; }` })
export class RoleAssignmentDialog {
  readonly data = inject<RoleDialogData>(MAT_DIALOG_DATA); readonly dialog = inject(MatDialogRef<RoleAssignmentDialog>);
  private readonly api = inject(Api); private readonly session = inject(SessionService); private readonly fb = inject(FormBuilder);
  readonly catalogue = this.api.resource(() => '/api/ui/administration/role-catalogue', arr(text, 100));
  readonly choices = () => roleChoices(this.data.user.accountType, this.catalogue.data() ?? []);
  readonly form = this.fb.nonNullable.group({ role: [this.choices().initialRole, Validators.required], scopeKind: ['CLIENT', Validators.required], clientId: '', engagementId: '', groupId: '', replacesGrantId: '',
    effectiveFrom: '', expiresAt: '', reason: ['', [Validators.required, Validators.minLength(5), Validators.maxLength(1000)]] });
  readonly confirmed = this.fb.nonNullable.control(false);
  readonly review = signal<ReturnType<typeof roleReview> | null>(null); readonly busy = signal(false); readonly uncertain = signal(false); readonly message = signal(''); readonly failed = signal(false);
  private reviewedRequest: ReturnType<RoleAssignmentDialog['request']> | null = null;
  private readonly openedGeneration = this.session.invalidation();
  constructor() { effect(() => { if (this.session.invalidation() !== this.openedGeneration || !this.session.current()?.staff) this.dialog.close(); }); const sub = this.form.valueChanges.subscribe(() => { this.review.set(null); this.confirmed.setValue(false); this.reviewedRequest = null; }); inject(DestroyRef).onDestroy(() => sub.unsubscribe()); }
  private request() {
    const f = this.form.getRawValue(); const scope = f.scopeKind;
    return { userId: this.data.user.userId, role: f.role, scopeKind: scope, clientId: scope === 'CLIENT' || scope === 'ENGAGEMENT' ? f.clientId || null : null,
      engagementId: scope === 'ENGAGEMENT' ? f.engagementId || null : null, groupId: scope === 'GROUP' ? f.groupId || null : null,
      effectiveFrom: f.effectiveFrom ? new Date(f.effectiveFrom).toISOString() : null, expiresAt: f.expiresAt ? new Date(f.expiresAt).toISOString() : null,
      replacesGrantId: f.replacesGrantId || null, reason: f.reason, confirmScopeExpansion: false };
  }
  async preview(): Promise<void> {
    if (this.form.invalid || this.busy() || this.uncertain()) return; const generation = this.session.invalidation(); this.busy.set(true); this.message.set('');
    try { const request = this.request(); const r = await this.api.command('/api/ui/administration/access/preview', request);
      if (generation !== this.session.invalidation()) { this.dialog.close(); return; }
      this.failed.set(!r.ok); if (r.ok) { this.review.set(decode(roleReview, r.value)); this.reviewedRequest = request; } else this.message.set(r.message);
    } catch { this.failed.set(true); this.message.set('The access review could not be loaded. Check dates and refresh.'); } finally { this.busy.set(false); }
  }
  async save(): Promise<void> {
    const p = this.review(), request = this.reviewedRequest; if (!p || !request || !this.confirmed.value || this.busy() || this.uncertain() || p.review.blockingReasons.length) return;
    const generation = this.session.invalidation(); this.busy.set(true);
    try { const r = await this.api.command('/api/ui/administration/access/assign', { request: { ...request, confirmScopeExpansion: true }, reviewDigest: p.digest, reviewed: true });
      if (generation !== this.session.invalidation()) { this.dialog.close(); return; }
      if (r.ok) this.dialog.close(true); else { this.failed.set(true); this.message.set(r.message); if (r.unknown) this.uncertain.set(true); else this.review.set(null); }
    } finally { this.busy.set(false); }
  }
}

@Component({ selector: 'audit-access-revocation', imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, ...SHARED], template: `
  <h2 mat-dialog-title>Revoke AuditSphere access</h2><mat-dialog-content><p>{{ data.role }} · {{ data.scopeKind }}</p>
    <p>This removes the local grant and invalidates protected sessions. Microsoft Entra roles remain separate.</p>
    <form [formGroup]="form" (ngSubmit)="save()"><label for="revoke-reason">Reason</label><textarea id="revoke-reason" formControlName="reason" maxlength="1000"></textarea>
      <label><input type="checkbox" formControlName="reviewed" /> I reviewed this grant and confirm revocation.</label>
      <button matButton="filled" [disabled]="form.invalid || busy() || uncertain()">Revoke reviewed access</button></form>
    <audit-command-message [message]="message()" [failed]="true" /></mat-dialog-content><mat-dialog-actions><button matButton (click)="dialog.close()" [disabled]="busy()">Close</button></mat-dialog-actions>
` })
export class AccessRevocationDialog {
  readonly data = inject<ReturnType<typeof accessLine>>(MAT_DIALOG_DATA); readonly dialog = inject(MatDialogRef<AccessRevocationDialog>);
  private readonly api = inject(Api); private readonly session = inject(SessionService);
  readonly form = inject(FormBuilder).nonNullable.group({ reason: ['', [Validators.required, Validators.minLength(5), Validators.maxLength(1000)]], reviewed: [false, Validators.requiredTrue] });
  readonly busy = signal(false); readonly uncertain = signal(false); readonly message = signal('');
  private readonly openedGeneration = this.session.invalidation();
  constructor() { effect(() => { if (this.session.invalidation() !== this.openedGeneration || !this.session.current()?.staff) this.dialog.close(); }); }
  async save(): Promise<void> { if (this.form.invalid || this.busy() || this.uncertain()) return; const generation = this.session.invalidation(); this.busy.set(true);
    try { const r = await this.api.command('/api/ui/administration/access/revoke', { grantId: this.data.grantId, groupGrant: this.data.groupGrant, ...this.form.getRawValue() });
      if (generation !== this.session.invalidation()) { this.dialog.close(); return; }
      if (r.ok) this.dialog.close(true); else { this.message.set(r.message); if (r.unknown) this.uncertain.set(true); }
    } finally { this.busy.set(false); }
  }
}
