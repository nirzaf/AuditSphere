import { Component, HostListener, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { SessionService } from '../../core/session';
import { PendingRequestReference, pendingRequestReference, TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { decodeReconciliationContext, decodeReconciliationLookup, decodeReconciliationPreview,
  decodeReconciliationReceipt, decodeReconciliationState, ReconciliationSource } from './analysis-preparation-contracts';

type Model={area:string;accountCodes:string;asOfDate:string;agingBasis:string;agingBucketRuleVersion:string;reason:string;evidenceReference:string};
const empty=():Model=>({area:'',accountCodes:'',asOfDate:new Date().toISOString().slice(0,10),agingBasis:'',agingBucketRuleVersion:'',reason:'',evidenceReference:''});
export function reconciliationEditableFields(raw:unknown):Model|null{
  if(!raw||typeof raw!=='object'||Array.isArray(raw))return null;const x=raw as Record<string,unknown>;
  return Object.keys(x).length===7&&Object.values(x).every(v=>typeof v==='string')&&
    String(x['area']).length<=80&&String(x['accountCodes']).length<=12000&&String(x['asOfDate']).length===10&&
    String(x['agingBasis']).length<=30&&String(x['agingBucketRuleVersion']).length<=60&&String(x['reason']).length<=4000&&
    String(x['evidenceReference']).length<=2000?x as Model:null;
}
type Intent={requestId:string;reviewBasis:string;source:ReconciliationSource;model:Model;reviewed:false};

@Component({selector:'audit-reconciliation-create',imports:[RouterLink,MatButtonModule,...SHARED],templateUrl:'./reconciliation-create.html',
  styles:[`.panel{overflow-wrap:anywhere}label{display:block;margin-block:.8rem}input,select,textarea{max-width:100%;box-sizing:border-box}textarea{width:min(100%,48rem)}.actions{display:flex;gap:.75rem;flex-wrap:wrap}.digest{overflow-wrap:anywhere}dt{font-weight:600;margin-block-start:.5rem}dd{margin-inline-start:0}`]})
export class ReconciliationCreate implements NavigationProtected{
  private readonly api=inject(Api);private readonly session=inject(SessionService);private readonly drafts=inject(TabDrafts);private readonly dialog=inject(MatDialog);
  private readonly route=inject(ActivatedRoute);private readonly router=inject(Router);readonly id=routeGuid();
  readonly context=this.api.resource(()=>this.id()?`/api/ui/engagements/${this.id()}/reconciliation-preparation`:null,decodeReconciliationContext);
  readonly selected=signal<ReconciliationSource|null>(null);readonly model=signal(empty());
  readonly state=this.api.resource(()=>{const id=this.id(),s=this.selected();return id&&s?`/api/ui/engagements/${id}/reconciliation-preparation/state?sourceKind=${s.kind}&sourceId=${s.id}`:null;},decodeReconciliationState);
  readonly valid=computed(()=>{const m=this.model(),s=this.state.data();const codes=this.codes();return!!s?.canPrepare&&m.area.trim().length>0&&m.area.trim().length<=80&&
    codes.length>0&&codes.length<=100&&new Set(codes).size===codes.length&&!!m.asOfDate&&!!m.reason.trim()&&m.reason.length<=4000&&!!m.evidenceReference.trim()&&
    m.evidenceReference.length<=2000&&(!this.needsAgeing()||!!m.agingBasis&&!!m.agingBucketRuleVersion);});
  readonly preview=signal<ReturnType<typeof decodeReconciliationPreview>|null>(null);readonly receipt=signal<ReturnType<typeof decodeReconciliationReceipt>|null>(null);
  readonly pending=signal<PendingRequestReference|null>(null);readonly busy=signal(false);readonly reviewed=signal(false);readonly uncertain=signal(false);readonly absent=signal(false);
  readonly message=signal('');readonly draftAvailable=signal(false);private request:Intent|null=null;private generation=0;
  private baseline=signal(JSON.stringify(empty()));readonly dirty=computed(()=>JSON.stringify(this.model())!==this.baseline());
  readonly codes=computed(()=>this.model().accountCodes.split(/[\n,]/).map(x=>x.trim()).filter(Boolean));
  readonly needsAgeing=computed(()=>/RECEIVABLE|PAYABLE/i.test(this.model().area));
  constructor(){
    effect(()=>{this.id();this.session.invalidation();untracked(()=>{this.generation++;this.model.set(empty());this.baseline.set(JSON.stringify(this.model()));this.selected.set(null);this.preview.set(null);this.receipt.set(null);this.pending.set(null);this.request=null;this.busy.set(false);this.reviewed.set(false);this.uncertain.set(false);this.absent.set(false);this.message.set('');this.draftAvailable.set(false);});});
    effect(()=>{const c=this.context.data();this.route.snapshot.queryParamMap; if(!c)return;untracked(()=>{if(!this.selected()){const queryId=this.route.snapshot.queryParamMap.get('sourceId');const queryKind=this.route.snapshot.queryParamMap.get('sourceKind');
      const found=c.sources.find(x=>x.id===queryId&&x.kind===queryKind)??c.sources[0]??null;if(found)this.selectSource(found,false);}});});
    effect(()=>{const s=this.state.data();if(!s)return;untracked(()=>{this.preview.set(null);this.reviewed.set(false);this.draftAvailable.set(this.drafts.read(this.scope(),reconciliationEditableFields).state==='ready');
      const p=this.drafts.readPendingRequest(this.scope(true));if(p.state==='ready'){this.pending.set(p.draft.value);this.uncertain.set(true);this.message.set(UNKNOWN_OUTCOME);}});});
    effect(()=>{this.model();untracked(()=>{this.preview.set(null);this.reviewed.set(false);});});
  }
  selectSource(source:ReconciliationSource|null,navigate=true){if(this.busy()||this.uncertain())return;this.selected.set(source);this.model.set(empty());this.baseline.set(JSON.stringify(this.model()));this.preview.set(null);this.reviewed.set(false);this.receipt.set(null);
    if(navigate&&source)this.router.navigate([], {relativeTo:this.route,queryParams:{sourceKind:source.kind,sourceId:source.id},queryParamsHandling:'merge',replaceUrl:true});}
  private scope(pending=false){const s=this.selected(),state=this.state.data();return{entity:`${pending?'reconciliation-request':'reconciliation-fields'}/${this.id()}/${s?.kind??'none'}/${s?.id??'none'}`,baseRevision:state?.reviewBasis??''};}
  private makeRequest(retained=this.pending()):Intent|null{const s=this.state.data(),source=this.selected();if(!s||!source)return null;
    const codes=this.codes();if(!codes.length||codes.length>100||new Set(codes).size!==codes.length)return null;
    return{requestId:retained?.requestId??crypto.randomUUID(),reviewBasis:s.reviewBasis,source,model:{...this.model()},reviewed:false};}
  private body(r:Intent,reviewed=false){return{requestId:r.requestId,reviewBasis:r.reviewBasis,reviewed,reason:r.model.reason.trim(),evidenceReference:r.model.evidenceReference.trim(),fields:{
    sourceKind:r.source.kind,sourceId:r.source.id,periodId:r.source.periodId,bookId:r.source.bookId,area:r.model.area.trim(),accountCodes:r.model.accountCodes.split(/[\n,]/).map(x=>x.trim()).filter(Boolean),
    asOfDate:r.model.asOfDate,agingBasis:r.model.agingBasis,agingBucketRuleVersion:r.model.agingBucketRuleVersion}};}
  async prepare(){const s=this.state.data(),g=this.generation;if(!s?.canPrepare||!this.valid()||this.busy()||(this.uncertain()&&!this.absent()))return;const r=this.makeRequest();if(!r)return;
    this.busy.set(true);this.preview.set(null);this.reviewed.set(false);try{const out=await this.api.command(`/api/ui/engagements/${this.id()}/reconciliation-preparation/preview`,this.body(r));if(g!==this.generation)return;
      if(!out.ok){this.message.set(out.message);return;}const p=decodeReconciliationPreview(out.value,'preview');if(p.engagementId!==this.id()||p.periodId!==s.periodId||p.sourceId!==s.sourceId||p.reviewBasis!==s.reviewBasis||this.state.data()?.reviewBasis!==s.reviewBasis||
        (this.pending()&&this.pending()!.requestHash!==p.requestHash))throw new Error('Changed preview');this.request=r;this.preview.set(p);this.message.set('');}
    catch{if(g===this.generation)this.message.set('The calculation preview could not be verified. Refresh the source context.');}finally{if(g===this.generation)this.busy.set(false);}}
  private matches(r:ReturnType<typeof decodeReconciliationReceipt>,p:PendingRequestReference){return r.reconciliationId===r.result.reconciliationId&&r.actorId===this.session.current()?.userId&&r.requestId===p.requestId&&r.requestHash===p.requestHash;}
  private clearPending(){this.drafts.clear(this.scope(true).entity);this.pending.set(null);this.uncertain.set(false);this.absent.set(false);this.request=null;}
  async execute(){const r=this.request,p=this.preview(),g=this.generation;if(!r||!p?.canProceed||!this.reviewed()||this.busy()||this.state.data()?.reviewBasis!==r.reviewBasis||
    JSON.stringify(r.model)!==JSON.stringify(this.model()))return;const ref={requestId:r.requestId,requestHash:p.requestHash};
    if(!this.drafts.save(this.scope(),this.model(),reconciliationEditableFields,true)||!this.drafts.save(this.scope(true),ref,pendingRequestReference,true)){this.message.set('This tab could not save a recovery reference. No reconciliation was sent.');return;}
    this.pending.set(ref);this.busy.set(true);this.reviewed.set(false);const out=await this.api.command(`/api/ui/engagements/${this.id()}/reconciliation-preparation`,this.body(r,true));if(g!==this.generation)return;
    this.busy.set(false);this.preview.set(null);if(!out.ok){this.message.set(out.message);if(out.unknown){this.uncertain.set(true);this.absent.set(false);}else this.clearPending();return;}
    try{const saved=decodeReconciliationReceipt(out.value,'receipt');if(!this.matches(saved,ref))throw new Error('Wrong receipt');this.receipt.set(saved);this.clearPending();this.drafts.clear(this.scope().entity);this.baseline.set(JSON.stringify(empty()));this.message.set('The source-bound reconciliation and immutable receipt are retained. Independent review remains separate.');this.context.reload();}
    catch{this.uncertain.set(true);this.message.set(UNKNOWN_OUTCOME);}}
  async reconcile(){const p=this.pending(),g=this.generation;if(!p||!this.state.data()||this.busy())return;this.busy.set(true);this.reviewed.set(false);this.preview.set(null);
    try{const v=await this.api.get(`/api/ui/engagements/${this.id()}/reconciliation-preparation/receipts/${p.requestId}?requestHash=${p.requestHash}`,decodeReconciliationLookup);if(g!==this.generation)return;
      if(v.found&&v.receipt&&this.matches(v.receipt,p)){this.receipt.set(v.receipt);this.absent.set(false);this.message.set('The retained receipt confirms this exact reconciliation. Acknowledge it before continuing.');}
      else if(!v.found){this.absent.set(true);this.message.set('No receipt is retained. Restore the identical fields and obtain a fresh preview and assent before retrying.');}else throw new Error('Wrong receipt');}
    catch{if(g===this.generation)this.message.set('Receipt verification is unavailable. Keep this request reference and check again.');}finally{if(g===this.generation)this.busy.set(false);}}
  acknowledge(){const r=this.receipt(),p=this.pending();if(!r||!p||!this.matches(r,p))return;this.clearPending();this.drafts.clear(this.scope().entity);this.model.set(empty());this.baseline.set(JSON.stringify(this.model()));this.receipt.set(null);this.context.reload();}
  saveDraft(){const ok=!this.busy()&&!this.uncertain()&&this.drafts.save(this.scope(),this.model(),reconciliationEditableFields);this.message.set(ok?'Editable fields saved in this tab; review assent is excluded.':'Draft unavailable.');return ok;}
  restoreDraft(){if(this.busy())return;const d=this.drafts.read(this.scope(),reconciliationEditableFields);if(d.state!=='ready'){this.message.set('The editable fields are stale or unavailable.');return;}this.model.set(d.draft.value);this.preview.set(null);this.reviewed.set(false);this.draftAvailable.set(false);}
  async confirmNavigation(){if(this.busy()||this.uncertain()){this.message.set('Resolve the retained reconciliation receipt before leaving.');return false;}if(!this.dirty())return true;
    const result=await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());return result==='save'?this.saveDraft():result==='discard'?this.discardDraft():false;}
  discardDraft(){if(this.busy()||this.uncertain())return false;this.model.set(empty());this.baseline.set(JSON.stringify(this.model()));this.preview.set(null);this.reviewed.set(false);this.drafts.clear(this.scope().entity);return true;}
  async refresh(){if(this.busy())return;if(!this.uncertain()&&this.dirty()&&!(await this.confirmNavigation()))return;this.preview.set(null);this.reviewed.set(false);this.state.reload();this.context.reload();}
  @HostListener('window:beforeunload',['$event']) beforeUnload(e:BeforeUnloadEvent){if(this.busy()||this.uncertain()||this.dirty()){e.preventDefault();e.returnValue='';}}
}
