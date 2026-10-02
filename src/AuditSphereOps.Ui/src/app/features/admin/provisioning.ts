import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Api } from '../../core/api';
import { arr, decode, text } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { accessWorkspace } from './access-contracts';
import { externalOperationResult } from './operations-contracts';

export type ProvisioningDialogData = { invite: boolean; tenantId: string; workspace: ReturnType<typeof accessWorkspace> };
const steps = ['Identity', 'Microsoft account details', 'AuditSphere role', 'Scope', 'Review', 'Create', 'Verify'];

@Component({ selector: 'audit-tenant-provisioning', imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatProgressBarModule, ...SHARED], template: `
  <h2 mat-dialog-title>{{ data.invite ? 'Invite new Microsoft guest' : 'Create Microsoft 365 user' }}</h2>
  <mat-dialog-content>
    <p>Tenant {{ data.tenantId }}. Microsoft permissions and local AuditSphere access are separate authorities.</p>
    <mat-progress-bar mode="determinate" [value]="(step() + 1) * 100 / steps.length" aria-label="Provisioning progress" />
    <p role="status">Step {{ step() + 1 }} of {{ steps.length }} · {{ steps[step()] }}</p>
    @if (step() < 5) {
      <form [formGroup]="form">
        @switch (step()) {
          @case (0) {
            @if (!data.invite) { <label for="new-directory-name">Display name</label><input id="new-directory-name" formControlName="displayName" maxlength="256" /> }
            <label for="new-directory-upn">{{ data.invite ? 'Approved guest email' : 'User principal name' }}</label><input id="new-directory-upn" formControlName="upn" type="email" maxlength="320" />
            @if(data.invite) { <p>Microsoft will send a B2B invitation only after your review. Entering a client contact does not send an invitation.</p> }
          }
          @case (1) {
            @if(data.invite) { <p>Microsoft manages guest authentication and invitation redemption. No password is collected or displayed.</p> }
            @else {
              <label for="new-directory-nickname">Mail nickname</label><input id="new-directory-nickname" formControlName="nickname" maxlength="64" />
              <label><input type="checkbox" formControlName="enabled" /> Account enabled</label>
              <p>A disabled account cannot receive AuditSphere access. The server generates an initial password in memory and Microsoft requires a password change at first sign-in. It is returned only by the original creation response.</p>
            }
          }
          @case (2) {
            @if(data.invite) { <p>AuditSphere role: ClientUser. No Entra administrator role is assigned.</p> }
            @else { <audit-state [loading]="roles.loading()" [error]="roles.error()" label="staff roles" />
              <label for="new-directory-role">AuditSphere role</label><select id="new-directory-role" formControlName="role">@for(r of roles.data() ?? []; track r) { @if(r !== 'ClientUser') { <option [value]="r">{{ r }}</option> } }</select>
            }
          }
          @case (3) {
            <label for="new-directory-scope">Scope type</label><select id="new-directory-scope" formControlName="scope">
              @if(!data.invite) { <option value="FIRM_WIDE">FIRM_WIDE</option> }<option value="CLIENT">CLIENT</option><option value="ENGAGEMENT">ENGAGEMENT</option>
            </select>
            @if(form.controls.scope.value !== 'FIRM_WIDE') {
              <label for="new-directory-client">Client</label><select id="new-directory-client" formControlName="clientId"><option value="">Select client</option>@for(c of data.workspace.clients; track c.id) { <option [value]="c.id">{{ c.name }}</option> }</select>
            }
            @if(form.controls.scope.value === 'ENGAGEMENT') {
              <label for="new-directory-engagement">Engagement</label><select id="new-directory-engagement" formControlName="engagementId"><option value="">Select engagement</option>@for(e of data.workspace.engagements; track e.id) { @if(e.parentId === form.controls.clientId.value) { <option [value]="e.id">{{ e.name }}</option> } }</select>
            }
            <p>Only the reviewed scope is granted. Microsoft group membership and Entra administrator roles are not assigned.</p>
          }
          @case (4) {
            <h3>Review identity and access</h3><dl><dt>Microsoft identity</dt><dd>{{ form.controls.upn.value }}</dd><dt>Tenant</dt><dd>{{ data.tenantId }}</dd>
              <dt>Local role</dt><dd>{{ data.invite ? 'ClientUser' : form.controls.role.value }}</dd><dt>Scope</dt><dd>{{ form.controls.scope.value }} · {{ scopeName() }}</dd></dl>
            <p>Current access: none. Proposed access: the selected local role and scope after Microsoft acceptance and the local grant transaction. Professional independence and segregation rules remain in force; practitioners must review the assignment.</p>
            <label for="new-directory-reason">Reason</label><textarea id="new-directory-reason" formControlName="reason" maxlength="1000"></textarea>
          }
        }
      </form>
      @if(step() === 4) {
        <label><input type="checkbox" [formControl]="reviewed" /> I reviewed the identity, tenant, local role, scope and reason.</label>
        @if(!data.invite) { <label><input type="checkbox" [formControl]="passwordReviewed" /> I accept the one-time initial password flow and will handle it through the approved secure process.</label> }
      }
    }
    @if(result(); as r) {
      <h3>Microsoft operation result</h3><audit-status [value]="r.state" /><p>{{ r.message }}</p><p>Operation {{ r.operationId }} · correlation {{ r.correlationId ?? 'Not returned' }}</p>
      @if(r.boundUserId) { <p>Local identity {{ r.boundUserId }} · role grant {{ r.roleGrantId ?? 'Not bound' }}</p> }
      @if(password()) {
        <section aria-label="One-time initial password"><h3>One-time initial password</h3>
          <p>This value is never saved in AuditSphere. It disappears after 60 seconds, when this page is hidden, when your session changes, or when this dialog closes. If lost, use your approved Microsoft password-reset process.</p>
          <label for="temporary-directory-password">Initial password</label><input id="temporary-directory-password" readonly autocomplete="off" [type]="revealed() ? 'text' : 'password'" [value]="password()" />
          <button matButton (click)="revealed.set(!revealed())">{{ revealed() ? 'Hide password' : 'Reveal password' }}</button>
          <button matButton (click)="clearPassword()">I have handled the password; clear it</button>
        </section>
      }
      <p>Refresh Users & Access after closing to review the persisted local binding and immutable history. Unknown results require operation reconciliation before another create/invite request.</p>
    }
    @if(uncertain()) { <p role="alert">The response was not confirmed. Close this dialog and inspect Microsoft operation history before another request. No automatic retry is made.</p> }
    <audit-command-message [message]="message()" [failed]="failed()" />
  </mat-dialog-content>
  <mat-dialog-actions>
    @if(step() < 5) {
      <button matButton (click)="back()" [disabled]="step() === 0 || busy()">Back</button>
      @if(step() < 4) { <button matButton="filled" (click)="next()" [disabled]="!canNext() || busy()">Next</button> }
      @else { <button matButton="filled" (click)="submit()" [disabled]="!canSubmit()">{{ data.invite ? 'Send reviewed Microsoft invitation' : 'Create reviewed Microsoft user' }}</button> }
    }
    <button matButton (click)="close()" [disabled]="busy()">Close</button>
  </mat-dialog-actions>
`, styles: `form { display:grid; gap:.5rem; } label { margin-top:.5rem; } input,select,textarea { max-width:100%; } dl { overflow-wrap:anywhere; }` })
export class TenantProvisioningDialog {
  readonly data = inject<ProvisioningDialogData>(MAT_DIALOG_DATA); readonly dialog = inject(MatDialogRef<TenantProvisioningDialog>);
  private readonly api = inject(Api); private readonly session = inject(SessionService); private readonly fb = inject(FormBuilder);
  readonly roles = this.api.resource(() => this.data.invite ? null : '/api/ui/administration/role-catalogue', arr(text, 100));
  readonly steps = steps; readonly step = signal(0); readonly busy = signal(false); readonly uncertain = signal(false); readonly message = signal(''); readonly failed = signal(false);
  readonly result = signal<ReturnType<typeof externalOperationResult> | null>(null);
  readonly password = signal<string | null>(null); readonly revealed = signal(false);
  readonly reviewed = this.fb.nonNullable.control(false); readonly passwordReviewed = this.fb.nonNullable.control(false);
  readonly form = this.fb.nonNullable.group({ displayName: ['', Validators.maxLength(256)], upn: ['', [Validators.required, Validators.email, Validators.maxLength(320)]],
    nickname: ['', [Validators.maxLength(64), Validators.pattern(/^[A-Za-z0-9'._!#^~-]*$/)]], enabled: true, role: 'Staff', scope: 'CLIENT', clientId: '', engagementId: '', reason: ['', [Validators.required, Validators.minLength(5), Validators.maxLength(1000)]] });
  private readonly key = 'angular-tenant-' + crypto.randomUUID(); private readonly generation = this.session.invalidation();
  private disposed = false; private passwordTimer?: ReturnType<typeof setTimeout>;
  constructor() {
    this.dialog.disableClose = true;
    effect(() => { if(this.generation !== this.session.invalidation()) { this.clearPassword(); this.form.reset(); this.dialog.close(false); } });
    const edited = this.form.valueChanges.subscribe(() => { this.reviewed.setValue(false); this.passwordReviewed.setValue(false); });
    const clientChanged = this.form.controls.clientId.valueChanges.subscribe(() => this.form.controls.engagementId.setValue(''));
    const hidden = () => { if(document.hidden) this.clearPassword(); }; document.addEventListener('visibilitychange', hidden);
    inject(DestroyRef).onDestroy(() => { this.disposed = true; this.clearPassword(); this.form.reset(); this.result.set(null); edited.unsubscribe(); clientChanged.unsubscribe(); document.removeEventListener('visibilitychange', hidden); });
  }
  scopeName(): string { const v=this.form.getRawValue(); return v.scope === 'FIRM_WIDE' ? 'Firm' :
    (v.scope === 'ENGAGEMENT' ? this.data.workspace.engagements.find(e=>e.id===v.engagementId)?.name : this.data.workspace.clients.find(c=>c.id===v.clientId)?.name) ?? 'Select scope'; }
  private scopeValid(): boolean { const v=this.form.getRawValue(); return v.scope === 'FIRM_WIDE' ? !this.data.invite :
    this.data.workspace.clients.some(c=>c.id===v.clientId) && (v.scope === 'CLIENT' || v.scope === 'ENGAGEMENT' && this.data.workspace.engagements.some(e=>e.id===v.engagementId && e.parentId===v.clientId)); }
  canNext(): boolean {
    const v=this.form.getRawValue(); switch(this.step()) {
      case 0: return this.form.controls.upn.valid && (this.data.invite || !!v.displayName.trim() && this.form.controls.displayName.valid && v.upn.length<=113);
      case 1: return this.data.invite || !!v.nickname.trim() && this.form.controls.nickname.valid && v.enabled;
      case 2: return this.data.invite || !!this.roles.data()?.includes(v.role) && v.role !== 'ClientUser';
      case 3: return this.scopeValid(); default: return false;
    }
  }
  next(): void { if(this.canNext()) { this.step.update(v=>v+1); this.reviewed.setValue(false); this.passwordReviewed.setValue(false); } }
  back(): void { if(!this.busy() && this.step()>0 && this.step()<5) { this.step.update(v=>v-1); this.reviewed.setValue(false); this.passwordReviewed.setValue(false); } }
  canSubmit(): boolean { return this.step()===4 && !this.busy() && !this.uncertain() && this.form.valid && this.scopeValid() && this.reviewed.value && (this.data.invite || this.passwordReviewed.value); }
  async submit(): Promise<void> {
    if(!this.canSubmit() || this.disposed) return;
    const v=this.form.getRawValue(); const clientId=v.scope==='FIRM_WIDE'?null:v.clientId; const engagementId=v.scope==='ENGAGEMENT'?v.engagementId:null;
    const request=this.data.invite ? { idempotencyKey:this.key, email:v.upn.trim(), clientId, engagementId, reason:v.reason.trim() } :
      { idempotencyKey:this.key, displayName:v.displayName.trim(), userPrincipalName:v.upn.trim(), mailNickname:v.nickname.trim(), accountEnabled:v.enabled, role:v.role, scopeKind:v.scope, clientId, engagementId, reason:v.reason.trim() };
    this.busy.set(true); this.step.set(5); this.message.set('');
    try {
      const outcome=await this.api.command('/api/ui/administration/directory/'+(this.data.invite?'invite':'create'), { request, reviewed:true, reviewedPasswordHandling:this.passwordReviewed.value });
      if(this.disposed || this.generation!==this.session.invalidation()) return;
      this.failed.set(!outcome.ok);
      if(outcome.ok) {
        const r=decode(externalOperationResult, outcome.value); this.result.set({...r,temporaryPassword:null});
        if(!this.data.invite && r.temporaryPassword) { this.password.set(r.temporaryPassword); this.passwordTimer=setTimeout(()=>this.clearPassword(),60000); }
        this.step.set(6);
      } else { this.message.set(outcome.message); this.uncertain.set(outcome.unknown); this.step.set(outcome.unknown?6:4); this.reviewed.setValue(false); this.passwordReviewed.setValue(false); }
    } catch { if(!this.disposed) { this.uncertain.set(true); this.failed.set(true); this.message.set('The Microsoft outcome could not be verified. Inspect operation history before repeating.'); this.step.set(6); } }
    finally { if(!this.disposed) this.busy.set(false); }
  }
  clearPassword(): void { if(this.passwordTimer) clearTimeout(this.passwordTimer); this.passwordTimer=undefined; this.password.set(null); this.revealed.set(false); }
  close(): void { if(!this.busy()) { this.clearPassword(); this.dialog.close(!!this.result() || this.uncertain()); } }
}
