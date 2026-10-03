import { Component, HostListener, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormField, form, maxLength, required } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { SessionService } from '../../core/session';
import { PendingRequestReference, pendingRequestReference, TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { ValuationEditableFields, allValuationAmounts, decodeValuationLookup, decodeValuationPreparation, decodeValuationPreview,
  decodeValuationReceipt, eclAmounts, inventoryAmounts, valuationEditableFields, valuationLabels } from './valuation-preparation-contracts';

const empty=(kind:string|null):ValuationEditableFields=>({ method:kind==='ECL'?'PROVISION_MATRIX_V1':'LOWER_COST_NRV_V1',methodologyVersion:'',assumptionsHash:'',
  probabilityOfDefault:'',lossGivenDefault:'',managementOverlay:'',managementExpectedLoss:'',bookedAmount:'',quantity:'',unitCost:'',nrvPerUnit:'',obsolescenceReserve:'',bookAmount:'',reason:'',evidenceReference:'' });
type Intent={ requestId:string;reviewBasis:string;fields:ReturnType<ValuationPreparation['requestFields']>;reviewed:boolean };
@Component({selector:'audit-valuation-preparation',imports:[RouterLink,FormField,MatButtonModule,...SHARED],templateUrl:'./valuation-preparation.html',
  styles:[`.panel { overflow-wrap:anywhere; } label { display:block; margin-block:.75rem; } textarea { width:100%; box-sizing:border-box; } input:not([type=checkbox]) { max-width:100%; box-sizing:border-box; }`]})
export class ValuationPreparation implements NavigationProtected {
  private readonly api=inject(Api);private readonly session=inject(SessionService);private readonly drafts=inject(TabDrafts);private readonly dialog=inject(MatDialog);
  private readonly route=inject(ActivatedRoute);private readonly params=toSignal(this.route.paramMap,{initialValue:this.route.snapshot?.paramMap});
  readonly id=routeGuid();readonly kind=computed(()=>{const k=this.params()?.get('kind');return k==='ECL'||k==='INVENTORY'?k:null;});
  readonly amounts=computed(()=>this.kind()==='ECL'?eclAmounts:inventoryAmounts);readonly labels=valuationLabels;
  readonly model=signal(empty(this.kind()));
  readonly fields=form(this.model,p=>{required(p.methodologyVersion);maxLength(p.methodologyVersion,100);required(p.assumptionsHash);maxLength(p.assumptionsHash,64);
    required(p.reason);maxLength(p.reason,4000);required(p.evidenceReference);maxLength(p.evidenceReference,2000);
    for(const name of allValuationAmounts){required(p[name],{when:()=>this.amounts().includes(name as never)});maxLength(p[name],21);}});
  readonly data=this.api.resource(()=>this.base(),(raw,path)=>{const s=decodeValuationPreparation(raw,path);if(s.kind!==this.kind()||s.context.id!==this.id())throw new Error('Wrong valuation target');return s;});
  readonly valid=computed(()=>this.fields().valid()&&/^[a-f0-9]{64}$/.test(this.model().assumptionsHash)&&
    this.amounts().every(k=>/^-?[0-9]{1,13}(?:\.[0-9]{1,6})?$/.test(this.model()[k])));
  readonly preview=signal<ReturnType<typeof decodeValuationPreview>|null>(null);readonly receipt=signal<ReturnType<typeof decodeValuationReceipt>|null>(null);
  readonly busy=signal(false);readonly reviewed=signal(false);readonly uncertain=signal(false);readonly absent=signal(false);readonly message=signal('');readonly draftAvailable=signal(false);
  readonly pending=signal<PendingRequestReference|null>(null);private readonly baseline=signal(JSON.stringify(empty(this.kind())));readonly dirty=computed(()=>JSON.stringify(this.model())!==this.baseline());
  private generation=0;private request:Intent|null=null;
  constructor(){effect(()=>{this.id();this.kind();this.session.invalidation();untracked(()=>{this.generation++;this.model.set(empty(this.kind()));this.baseline.set(JSON.stringify(this.model()));
    this.preview.set(null);this.receipt.set(null);this.busy.set(false);this.reviewed.set(false);this.uncertain.set(false);this.absent.set(false);this.pending.set(null);this.message.set('');this.draftAvailable.set(false);this.request=null;});});
    effect(()=>{const s=this.data.data();if(!s)return;untracked(()=>{this.preview.set(null);this.reviewed.set(false);this.draftAvailable.set(this.drafts.read(this.scope(),valuationEditableFields).state==='ready');
      const p=this.drafts.readPendingRequest(this.scope(true));if(p.state==='ready'){this.pending.set(p.draft.value);this.uncertain.set(true);this.message.set(UNKNOWN_OUTCOME);}});});
    effect(()=>{this.model();untracked(()=>{this.preview.set(null);this.reviewed.set(false);});});}
  private base(){return this.kind()&&this.id()?'/api/ui/accounting/reconciliations/'+this.id()+'/valuation-preparation/'+this.kind():null;}
  private scope(pending=false){return{entity:(pending?'valuation-request/':'valuation-fields/')+this.kind()+'/'+this.id(),baseRevision:this.data.data()?.reviewBasis??''};}
  requestFields(){const m=this.model();return{method:m.method,methodologyVersion:m.methodologyVersion,assumptionsHash:m.assumptionsHash,
    probabilityOfDefault:this.kind()==='ECL'?m.probabilityOfDefault:null,lossGivenDefault:this.kind()==='ECL'?m.lossGivenDefault:null,
    managementOverlay:this.kind()==='ECL'?m.managementOverlay:null,managementExpectedLoss:this.kind()==='ECL'?m.managementExpectedLoss:null,
    bookedAmount:this.kind()==='ECL'?m.bookedAmount:null,quantity:this.kind()==='INVENTORY'?m.quantity:null,unitCost:this.kind()==='INVENTORY'?m.unitCost:null,
    nrvPerUnit:this.kind()==='INVENTORY'?m.nrvPerUnit:null,obsolescenceReserve:this.kind()==='INVENTORY'?m.obsolescenceReserve:null,bookAmount:this.kind()==='INVENTORY'?m.bookAmount:null,
    reason:m.reason,evidenceReference:m.evidenceReference};}
  async prepare(){const s=this.data.data(),g=this.generation;if(!s?.canPrepare||!this.valid()||this.busy()||(this.uncertain()&&!this.absent()))return;
    const retained=this.pending(),r:Intent={requestId:retained?.requestId??crypto.randomUUID(),reviewBasis:s.reviewBasis,fields:this.requestFields(),reviewed:false};
    this.busy.set(true);this.preview.set(null);this.reviewed.set(false);try{const out=await this.api.command(this.base()+'/preview',r);if(g!==this.generation)return;
      if(!out.ok){this.message.set(out.message);return;}const p=decodeValuationPreview(out.value,'preview');if(p.kind!==s.kind||p.reconciliationId!==s.context.id||p.reviewBasis!==s.reviewBasis||
        p.currency!==s.context.currency||this.data.data()?.reviewBasis!==s.reviewBasis||(retained&&retained.requestHash!==p.requestHash))throw new Error('Changed preview');
      this.request=r;this.preview.set(p);this.message.set('');}catch{if(g===this.generation)this.message.set('The current calculation preview could not be verified. Refresh the source context.');}
    finally{if(g===this.generation)this.busy.set(false);}}
  private matches(r:ReturnType<typeof decodeValuationReceipt>,p:PendingRequestReference){return r.kind===this.kind()&&r.reconciliationId===this.id()&&r.actorId===this.session.current()?.userId&&r.requestId===p.requestId&&r.requestHash===p.requestHash;}
  private clearPending(){this.drafts.clear(this.scope(true).entity);this.pending.set(null);this.uncertain.set(false);this.absent.set(false);this.request=null;}
  async execute(){const r=this.request,p=this.preview(),g=this.generation;if(!r||!p?.canProceed||!this.reviewed()||this.busy()||this.data.data()?.reviewBasis!==r.reviewBasis||
    JSON.stringify(r.fields)!==JSON.stringify(this.requestFields()))return;const ref={requestId:r.requestId,requestHash:p.requestHash};
    if(!this.drafts.save(this.scope(),this.model(),valuationEditableFields,true)||!this.drafts.save(this.scope(true),ref,pendingRequestReference,true)){this.message.set('The recovery reference could not be saved in this tab. No valuation was sent.');return;}
    this.pending.set(ref);this.busy.set(true);this.reviewed.set(false);const out=await this.api.command(this.base()!,{...r,reviewed:true});if(g!==this.generation)return;
    this.busy.set(false);this.preview.set(null);if(!out.ok){this.message.set(out.message);if(out.unknown){this.uncertain.set(true);this.absent.set(false);}else this.clearPending();return;}
    try{const receipt=decodeValuationReceipt(out.value,'receipt');if(!this.matches(receipt,ref))throw new Error('Wrong receipt');this.receipt.set(receipt);this.clearPending();this.drafts.clear(this.scope().entity);
      this.baseline.set(JSON.stringify(this.model()));this.message.set('The valuation, exact source lineage and immutable receipt are retained. Independent review is still required.');this.data.reload();}
    catch{this.uncertain.set(true);this.message.set(UNKNOWN_OUTCOME);}}
  async reconcile(){const p=this.pending(),g=this.generation;if(!p||!this.data.data()||this.busy())return;this.busy.set(true);this.reviewed.set(false);this.preview.set(null);
    try{const out=await this.api.get(this.base()+'/receipts/'+p.requestId+'?requestHash='+p.requestHash,decodeValuationLookup);if(g!==this.generation)return;
      if(out.found&&out.receipt&&this.matches(out.receipt,p)){this.receipt.set(out.receipt);this.absent.set(false);this.message.set('The retained receipt confirms preparation. Acknowledge it before continuing.');}
      else if(!out.found){this.absent.set(true);this.message.set('No receipt is retained. Restore the identical original fields and obtain fresh preview and assent before retrying the same request.');}
      else throw new Error('Wrong receipt');}catch{if(g===this.generation)this.message.set('Receipt verification is unavailable. Keep this reference and check again.');}
    finally{if(g===this.generation)this.busy.set(false);}}
  acknowledge(){const r=this.receipt(),p=this.pending();if(!r||!p||!this.matches(r,p))return;this.clearPending();this.drafts.clear(this.scope().entity);this.model.set(empty(this.kind()));this.baseline.set(JSON.stringify(this.model()));this.data.reload();}
  saveDraft(){const ok=!this.busy()&&!this.uncertain()&&this.drafts.save(this.scope(),this.model(),valuationEditableFields);this.message.set(ok?'Editable fields saved in this tab; assent is excluded.':'Draft unavailable.');return ok;}
  restoreDraft(){if(this.busy())return;const d=this.drafts.read(this.scope(),valuationEditableFields);if(d.state!=='ready'){this.message.set('The editable fields are stale or unavailable.');return;}this.model.set(d.draft.value);this.preview.set(null);this.reviewed.set(false);this.draftAvailable.set(false);}
  discardDraft(){if(this.busy()||this.uncertain())return false;this.model.set(JSON.parse(this.baseline()));this.preview.set(null);this.reviewed.set(false);this.drafts.clear(this.scope().entity);return true;}
  async confirmNavigation(){if(this.busy()||this.uncertain()){this.message.set('Resolve the retained preparation receipt before leaving.');return false;}if(!this.dirty())return true;
    const d=await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());return d==='save'?this.saveDraft():d==='discard'?this.discardDraft():false;}
  async refresh(){if(this.busy())return;if(!this.uncertain()&&this.dirty()&&!(await this.confirmNavigation()))return;this.preview.set(null);this.reviewed.set(false);this.data.reload();}
  @HostListener('window:beforeunload',['$event']) beforeUnload(e:BeforeUnloadEvent){if(this.busy()||this.uncertain()||this.dirty()){e.preventDefault();e.returnValue='';}}
}
