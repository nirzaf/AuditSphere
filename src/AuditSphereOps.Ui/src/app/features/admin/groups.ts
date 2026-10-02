import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { accessWorkspace } from './access-contracts';
import { groupChangeResult, groupMembers, groupMembershipPreview } from './operations-contracts';

@Component({ selector:'audit-managed-groups', imports:[ReactiveFormsModule,MatDialogModule,MatButtonModule,...SHARED], template:`
  <h2 mat-dialog-title>Managed Microsoft groups</h2><mat-dialog-content>
    <p>Only allowlisted AuditSphere-managed groups can be changed. Microsoft membership may grant access to linked Microsoft resources. It never changes AuditSphere roles or scopes. Dynamic and role-assignable groups are refused.</p>
    <details><summary>Approve a managed group</summary><form [formGroup]="approval" (ngSubmit)="approve()">
      <label for="managed-group-object">Group object ID</label><input id="managed-group-object" formControlName="objectId" maxlength="36" />
      <label for="managed-group-purpose">Purpose</label><input id="managed-group-purpose" formControlName="purpose" maxlength="300" />
      <label for="managed-group-reason">Approval reason</label><textarea id="managed-group-reason" formControlName="reason" maxlength="1000"></textarea>
      <label><input type="checkbox" [formControl]="approvalReviewed" /> I reviewed this exact group, collaboration access, purpose and reason.</label>
      <button matButton="filled" [disabled]="approval.invalid || !approvalReviewed.value || busy() || uncertain()">Approve reviewed managed group</button>
    </form></details>
    <form [formGroup]="change">
      <label for="managed-group-select">Managed group</label><select id="managed-group-select" formControlName="groupId"><option value="">Select managed group</option>@for(g of workspace().groups;track g.id){ <option [value]="g.id">{{ g.displayName }} · {{ g.purpose }}</option> }</select>
      <button matButton type="button" (click)="loadMembers(null)" [disabled]="!change.controls.groupId.value || busy()">View members</button>
      <label for="managed-group-user">Local Microsoft-bound user</label><select id="managed-group-user" formControlName="userId"><option value="">Select user</option>@for(u of workspace().users;track u.userId){ <option [value]="u.userId">{{ u.displayName }} · {{ u.microsoftIdentity }}</option> }</select>
      <label for="managed-group-action">Membership action</label><select id="managed-group-action" formControlName="action"><option value="ADD">Add member</option><option value="REMOVE">Remove member</option></select>
      <label for="managed-membership-reason">Change reason</label><textarea id="managed-membership-reason" formControlName="reason" maxlength="1000"></textarea>
      <button matButton type="button" (click)="preview()" [disabled]="change.invalid || busy() || uncertain()">Review Microsoft membership</button>
    </form>
    @if(members();as page){ <h3>Observed members</h3><ul>@for(m of page.members;track m.objectId){ <li>{{ m.displayName }} · {{ m.userPrincipalName }} · {{ m.objectId }}</li> }@empty{ <li>No members in this page.</li> }</ul>
      <button matButton (click)="loadMembers(page.nextPageToken)" [disabled]="!page.nextPageToken || busy()">Next member page</button> }
    @if(review();as p){
      <h3>Review membership change</h3><dl><dt>Group</dt><dd>{{ p.groupName }} · {{ p.groupObjectId }}</dd><dt>User</dt><dd>{{ p.userName }} · {{ p.userObjectId }}</dd>
        <dt>Tenant</dt><dd>{{ p.tenantId }}</dd><dt>Existing membership</dt><dd>{{ p.existingMembership ? 'Member' : 'Not a member' }}</dd><dt>Requested change</dt><dd>{{ change.controls.action.value }}</dd>
        <dt>Actor</dt><dd>{{ actorId }}</dd><dt>Reason</dt><dd>{{ change.controls.reason.value }}</dd><dt>Observed</dt><dd>{{ p.observedAt }}</dd></dl>
      <label><input type="checkbox" [formControl]="reviewed" /> I reviewed the exact group, user, existing membership and requested collaboration access.</label>
      <button matButton="filled" (click)="save()" [disabled]="!reviewed.value || busy() || uncertain()">Apply reviewed membership change</button>
    }
    @if(result();as r){ <h3>Microsoft result</h3><audit-status [value]="r.state" /><p>{{ r.message }}</p><p>Operation {{ r.operationId ?? 'No change' }} · correlation {{ r.correlationId ?? 'Not returned' }}</p> }
    <p>Retiring the allowlist entry stops future AuditSphere administration. It does not delete the Microsoft group or remove existing members.</p>
    <label><input type="checkbox" [formControl]="retireReviewed" /> I reviewed the selected group and reason and want to retire its allowlist entry.</label>
    <button matButton (click)="retire()" [disabled]="!change.controls.groupId.value || change.controls.reason.invalid || !retireReviewed.value || busy() || uncertain()">Retire reviewed allowlist entry</button>
    <audit-command-message [message]="message()" [failed]="failed()" />
    @if(uncertain()){ <p role="alert">The outcome is unknown. Review operation history and reconcile before another membership change.</p> }
  </mat-dialog-content><mat-dialog-actions><button matButton (click)="dialog.close(changed)" [disabled]="busy()">Close</button></mat-dialog-actions>
`,styles:`form { display:grid; gap:.5rem; margin-block:1rem; } dl,li { overflow-wrap:anywhere; }` })
export class ManagedGroupsDialog {
  readonly dialog=inject(MatDialogRef<ManagedGroupsDialog>); readonly workspace=signal(inject<ReturnType<typeof accessWorkspace>>(MAT_DIALOG_DATA));
  private readonly api=inject(Api); private readonly session=inject(SessionService); private readonly fb=inject(FormBuilder);
  readonly approval=this.fb.nonNullable.group({objectId:['',[Validators.required,Validators.pattern(/^[0-9a-f-]{36}$/i)]],purpose:['',[Validators.required,Validators.maxLength(300)]],reason:['',[Validators.required,Validators.minLength(5),Validators.maxLength(1000)]]});
  readonly change=this.fb.nonNullable.group({groupId:['',Validators.required],userId:['',Validators.required],action:'ADD',reason:['',[Validators.required,Validators.minLength(5),Validators.maxLength(1000)]]});
  readonly approvalReviewed=this.fb.nonNullable.control(false); readonly reviewed=this.fb.nonNullable.control(false); readonly retireReviewed=this.fb.nonNullable.control(false);
  readonly members=signal<ReturnType<typeof groupMembers>|null>(null); readonly review=signal<ReturnType<typeof groupMembershipPreview>|null>(null); readonly result=signal<ReturnType<typeof groupChangeResult>|null>(null);
  readonly busy=signal(false);readonly uncertain=signal(false);readonly failed=signal(false);readonly message=signal(''); readonly actorId=this.session.current()?.userId ?? '';
  private readonly generation=this.session.invalidation(); private disposed=false; private key=''; changed=false;
  constructor(){ this.dialog.disableClose=true;
    effect(()=>{if(this.generation!==this.session.invalidation()){this.review.set(null);this.members.set(null);this.dialog.close(false);}});
    const changes=this.change.valueChanges.subscribe(()=>{this.review.set(null);this.reviewed.setValue(false);this.retireReviewed.setValue(false);this.key='';});
    const selected=this.change.controls.groupId.valueChanges.subscribe(()=>this.members.set(null));
    const approval=this.approval.valueChanges.subscribe(()=>this.approvalReviewed.setValue(false));
    inject(DestroyRef).onDestroy(()=>{this.disposed=true;changes.unsubscribe();selected.unsubscribe();approval.unsubscribe();this.change.reset();this.approval.reset();this.members.set(null);this.review.set(null);this.result.set(null);});
  }
  private current():boolean{return !this.disposed && this.generation===this.session.invalidation();}
  async approve():Promise<void>{if(this.approval.invalid||!this.approvalReviewed.value||this.busy()||this.uncertain())return;
    this.busy.set(true);try{const v=this.approval.getRawValue();const r=await this.api.command('/api/ui/administration/groups/approve',{groupObjectId:v.objectId,purpose:v.purpose,reason:v.reason,reviewed:true});
      if(!this.current())return;this.failed.set(!r.ok);if(r.ok){this.changed=true;this.message.set('Group allowlist approval recorded.');this.approval.reset();await this.reload();}else{this.message.set(r.message);this.uncertain.set(r.unknown);}}
    catch{if(this.current()){this.failed.set(true);this.uncertain.set(true);this.message.set('Approval could not be confirmed. Refresh the allowlist before repeating.');}}finally{if(this.current())this.busy.set(false);}}
  private async reload():Promise<void>{const w=await this.api.get('/api/ui/administration/access',accessWorkspace);if(this.current())this.workspace.set(w);}
  async loadMembers(pageToken:string|null):Promise<void>{const id=this.change.controls.groupId.value;if(!id||this.busy())return;this.busy.set(true);this.members.set(null);
    try{const r=await this.api.command('/api/ui/administration/groups/'+id+'/members',{pageToken});if(!this.current()||id!==this.change.controls.groupId.value)return;
      this.failed.set(!r.ok);if(r.ok)this.members.set(decode(groupMembers,r.value));else this.message.set(r.message);}catch{if(this.current()){this.failed.set(true);this.message.set('The member page could not be verified.');}}finally{if(this.current())this.busy.set(false);}}
  async preview():Promise<void>{if(this.change.invalid||this.busy()||this.uncertain())return;this.busy.set(true);this.review.set(null);this.reviewed.setValue(false);
    const v=this.change.getRawValue();const signature=JSON.stringify(v);
    try{const r=await this.api.command('/api/ui/administration/groups/'+v.groupId+'/preview',{userId:v.userId});if(!this.current()||signature!==JSON.stringify(this.change.getRawValue()))return;
      this.failed.set(!r.ok);if(r.ok){this.review.set(decode(groupMembershipPreview,r.value));this.key='angular-group-'+crypto.randomUUID();}else this.message.set(r.message);}
    catch{if(this.current()){this.failed.set(true);this.message.set('Current membership could not be verified. Nothing was changed.');}}finally{if(this.current())this.busy.set(false);}}
  async save():Promise<void>{const p=this.review();if(!p||!this.reviewed.value||this.busy()||this.uncertain())return;this.busy.set(true);const v=this.change.getRawValue();
    try{const r=await this.api.command('/api/ui/administration/groups/change',{request:{idempotencyKey:this.key,managedGroupId:p.managedGroupId,userId:p.userId,add:v.action==='ADD',reason:v.reason,expectedExistingMembership:p.existingMembership},reviewed:true});
      if(!this.current())return;this.failed.set(!r.ok);if(r.ok){const result=decode(groupChangeResult,r.value);this.result.set(result);this.changed=true;this.uncertain.set(['UNKNOWN','DISPATCHING'].includes(result.state));this.review.set(null);this.members.set(null);}else{this.message.set(r.message);this.uncertain.set(r.unknown);this.review.set(null);}}
    catch{if(this.current()){this.uncertain.set(true);this.failed.set(true);this.message.set('The membership outcome could not be confirmed. Reconcile before repeating.');}}finally{if(this.current())this.busy.set(false);}}
  async retire():Promise<void>{const v=this.change.getRawValue();if(!v.groupId||this.change.controls.reason.invalid||!this.retireReviewed.value||this.busy()||this.uncertain())return;this.busy.set(true);
    try{const r=await this.api.command('/api/ui/administration/groups/'+v.groupId+'/retire',{reason:v.reason,reviewed:true});if(!this.current())return;
      this.failed.set(!r.ok);if(r.ok){this.changed=true;this.change.reset();this.message.set('Allowlist entry retired; Microsoft memberships were preserved.');await this.reload();}else{this.message.set(r.message);this.uncertain.set(r.unknown);}}
    catch{if(this.current()){this.failed.set(true);this.uncertain.set(true);this.message.set('Retirement could not be confirmed. Refresh the allowlist before repeating.');}}finally{if(this.current())this.busy.set(false);}}
}
