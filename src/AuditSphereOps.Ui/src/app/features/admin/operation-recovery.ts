import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { Api } from '../../core/api';
import { decode } from '../../core/decode';
import { SessionService } from '../../core/session';
import { SHARED } from '../../core/ui';
import { externalOperationResult, operationReview } from './operations-contracts';

@Component({selector:'audit-operation-recovery',imports:[ReactiveFormsModule,MatDialogModule,MatButtonModule,...SHARED],template:`
  <h2 mat-dialog-title>Review Microsoft operation recovery</h2><mat-dialog-content>
    <audit-state [loading]="review.loading()" [error]="review.error()" label="persisted Microsoft operation" />
    @if(review.data();as r){
      <dl><dt>Operation</dt><dd>{{ r.operationId }} · {{ r.kind }}</dd><dt>Recorded state</dt><dd>{{ r.state }}</dd><dt>Microsoft tenant / target</dt><dd>{{ r.tenantId }} · {{ r.target }} · {{ r.objectId ?? 'Not confirmed' }}</dd>
        <dt>Requested local role / scope</dt><dd>{{ r.role ?? 'No local role change' }} · {{ r.scopeKind ?? 'Microsoft membership only' }} · {{ r.clientId ?? r.managedGroupId ?? 'Firm' }} · {{ r.engagementId ?? '' }}</dd>
        <dt>Reason</dt><dd>{{ r.reason }}</dd><dt>Original actor</dt><dd>{{ r.requestedByUserId }}</dd><dt>Microsoft correlation</dt><dd>{{ r.correlationId ?? 'Not returned' }}</dd><dt>Reconciliation</dt><dd>{{ r.reconciliation ?? 'Not reconciled' }}</dd></dl>
      <p>Reconciliation observes the Microsoft result and may complete the exact previously reviewed local binding. It never blindly repeats user creation or guest invitation. The original temporary password is never returned again; use the approved Microsoft reset process if needed.</p>
      <label><input type="checkbox" [formControl]="reviewed" /> I reviewed this persisted intent and want to reconcile or complete its local binding.</label>
      <button matButton="filled" (click)="resume()" [disabled]="!reviewed.value || busy()">Reconcile reviewed operation</button>
    }
    @if(result();as r){<audit-status [value]="r.state" /><p>{{ r.message }}</p>}
    <audit-command-message [message]="message()" [failed]="failed()" />
  </mat-dialog-content><mat-dialog-actions><button matButton (click)="dialog.close(changed)" [disabled]="busy()">Close</button></mat-dialog-actions>
`,styles:`dl { overflow-wrap:anywhere; }`})
export class TenantOperationRecoveryDialog{
  private readonly api=inject(Api);private readonly session=inject(SessionService);readonly id=inject<string>(MAT_DIALOG_DATA);readonly dialog=inject(MatDialogRef<TenantOperationRecoveryDialog>);
  readonly review=this.api.resource(()=>'/api/ui/administration/microsoft365/operations/'+this.id,operationReview);
  readonly reviewed=inject(FormBuilder).nonNullable.control(false);readonly busy=signal(false);readonly message=signal('');readonly failed=signal(false);readonly result=signal<ReturnType<typeof externalOperationResult>|null>(null);
  private readonly generation=this.session.invalidation();private disposed=false;changed=false;
  constructor(){this.dialog.disableClose=true;effect(()=>{if(this.generation!==this.session.invalidation()){this.result.set(null);this.dialog.close(false);}if(!this.review.data())this.reviewed.setValue(false);});
    inject(DestroyRef).onDestroy(()=>{this.disposed=true;this.result.set(null);});}
  async resume():Promise<void>{if(!this.review.data()||!this.reviewed.value||this.busy())return;this.busy.set(true);
    try{const r=await this.api.command('/api/ui/administration/microsoft365/operations/'+this.id+'/resume',{reviewed:true});if(this.disposed||this.generation!==this.session.invalidation())return;
      this.failed.set(!r.ok);if(r.ok){const result=decode(externalOperationResult,r.value);if(result.temporaryPassword)throw new Error('Unsupported recovery response');this.result.set(result);this.changed=true;this.review.reload();}else{this.message.set(r.message);this.review.reload();}}
    catch{if(!this.disposed){this.failed.set(true);this.message.set('Recovery could not be confirmed. Refresh and review persisted state before another recovery action.');this.review.reload();}}
    finally{if(!this.disposed){this.reviewed.setValue(false);this.busy.set(false);}}}
}
