import { Component, HostListener, computed, effect, inject, signal, untracked } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { SessionService } from '../../core/session';
import { PendingRequestReference, pendingRequestReference, TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { obj, text } from '../../core/decode';
import { safeReturnPath, statementLinkContext, StatementLinkContext } from '../../core/statement-link';
import { AnalyticalEditableFields, analyticalEditableFields, decodeAnalyticalContext, decodeAnalyticalLookup,
  decodeAnalyticalPreview, decodeAnalyticalReceipt, decodeAnalyticalState } from './analytical-preparation-contracts';

const decodeStatementRevision = obj({ basis: obj({ revision: text }) });
const empty = (): AnalyticalEditableFields => ({ area:'', measure:'', currentAmount:'', priorAmount:'', budgetAmount:'',
  denominatorBasis:'', formulaVersion:'', explanation:'', seasonalityExplanation:'', reason:'', evidenceReference:'' });
type Intent = { requestId:string; reviewBasis:string; fields:AnalyticalEditableFields & {periodId:string;comparisonPeriodId:string|null}; reason:string; evidenceReference:string; reviewed:boolean };

@Component({ selector:'audit-analytical-preparation', imports:[RouterLink,MatButtonModule,...SHARED], templateUrl:'./analytical-preparation.html',
  styles:[`.panel{overflow-wrap:anywhere}.panel label{display:block;margin-block:.75rem}.panel input,.panel textarea,.panel select{max-width:100%;box-sizing:border-box}.panel textarea{width:100%}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(16rem,1fr));gap:0 1rem}dd{overflow-wrap:anywhere}`] })
export class AnalyticalPreparation implements NavigationProtected {
  private readonly api=inject(Api); private readonly session=inject(SessionService); private readonly drafts=inject(TabDrafts);
  private readonly dialog=inject(MatDialog); private readonly route=inject(ActivatedRoute);
  private readonly routeParams=toSignal(this.route.paramMap,{initialValue:this.route.snapshot?.paramMap});
  private readonly router=inject(Router);
  private readonly statementLink:StatementLinkContext|null=statementLinkContext(this.route.snapshot?.queryParamMap);
  private statementApplied=false;
  readonly returnTarget=computed(()=>safeReturnPath(this.statementLink?.returnUrl??null));
  readonly id=routeGuid(); readonly model=signal(empty()); readonly periodId=signal(''); readonly comparisonPeriodId=signal('');
  readonly data=this.api.resource(()=>this.id()?this.base():null,decodeAnalyticalContext,
    'This analytical workspace is unavailable to your current account. Ask your firm administrator to confirm your AuditSphere role and client or engagement scope.');
  readonly periods=computed(()=>this.data.data()?.periods??[]);
  readonly currentPeriod=computed(()=>this.periods().find(p=>p.id===this.periodId())??null);
  readonly comparisonPeriods=computed(()=>{const p=this.currentPeriod();return p?this.periods().filter(x=>x.id!==p.id&&x.endDate<p.startDate):[];});
  readonly basis=signal<ReturnType<typeof decodeAnalyticalState>|null>(null);
  readonly preview=signal<ReturnType<typeof decodeAnalyticalPreview>|null>(null);
  readonly receipt=signal<ReturnType<typeof decodeAnalyticalReceipt>|null>(null);
  readonly pending=signal<PendingRequestReference|null>(null); readonly busy=signal(false); readonly reviewed=signal(false);
  readonly uncertain=signal(false); readonly absent=signal(false); readonly draftAvailable=signal(false); readonly message=signal('');
  private request:Intent|null=null; private requestGeneration=0; private basisGeneration=0;
  private readonly baseline=signal(JSON.stringify(empty())); readonly dirty=computed(()=>JSON.stringify(this.model())!==this.baseline());
  readonly valid=computed(()=>{
    const m=this.model(); const amount=(v:string,optional=false)=>optional&&v===''||/^-?[0-9]{1,13}(?:\.[0-9]{1,6})?$/.test(v);
    return !!m.area.trim()&&m.area.length<=80&&!!m.measure.trim()&&m.measure.length<=100&&amount(m.currentAmount)&&amount(m.priorAmount)&&
      amount(m.budgetAmount,true)&&!!m.denominatorBasis.trim()&&m.denominatorBasis.length<=200&&!!m.formulaVersion.trim()&&m.formulaVersion.length<=100&&
      !!m.explanation.trim()&&m.explanation.length<=4000&&m.seasonalityExplanation.length<=2000&&!!m.reason.trim()&&m.reason.length<=4000&&
      !!m.evidenceReference.trim()&&m.evidenceReference.length<=2000;
  });

  constructor(){
    effect(()=>{this.id();this.session.invalidation();this.routeParams();untracked(()=>{this.requestGeneration++;this.basisGeneration++;this.reset();});});
    effect(()=>{const id=this.id(),c=this.data.data();if(!c||c.engagementId!==id)return;untracked(()=>{
      if(!c.periods.some(p=>p.id===this.periodId())){const linked=this.statementLink?c.periods.find(p=>p.startDate===this.statementLink!.periodStart&&p.endDate===this.statementLink!.periodEnd):undefined;const preferred=linked??c.periods.find(p=>p.canPrepare)??c.periods[0];this.periodId.set(preferred?.id??'');this.comparisonPeriodId.set('');}
      void this.loadBasis();
      if(this.statementLink&&!this.statementApplied){this.statementApplied=true;void this.applyStatementLink(this.statementLink);}});});
  }
  private async applyStatementLink(link:StatementLinkContext):Promise<void>{
    const id=this.id();
    try{
      const current=(await this.api.get(`/api/ui/engagements/${id}/statements/workspace`,decodeStatementRevision)).basis.revision;
      if(id!==this.id())return;
      const period=this.periods().find(p=>p.startDate===link.periodStart&&p.endDate===link.periodEnd);
      if(current!==link.revision||!period){this.message.set('This statement link points to an earlier version of the statement or to a period this engagement does not have. Open the statement again and choose the line; nothing has been filled in.');return;}
      this.model.set({...empty(),area:link.area,measure:link.line});
      this.message.set(`Pre-filled from statement line ${link.line}. Check every field before you prepare the test.`);
    }catch{
      this.message.set('The statement could not be checked, so nothing has been filled in. Open the statement again.');
    }
  }
  returnToStatement():void{const target=this.returnTarget();if(target)void this.router.navigateByUrl(target);}
  private base(){return `/api/ui/engagements/${this.id()}/analytical-preparation`;}
  private reset(){this.periodId.set('');this.comparisonPeriodId.set('');this.basis.set(null);this.model.set(empty());this.baseline.set(JSON.stringify(empty()));
    this.preview.set(null);this.receipt.set(null);this.pending.set(null);this.busy.set(false);this.reviewed.set(false);this.uncertain.set(false);this.absent.set(false);
    this.draftAvailable.set(false);this.message.set('');this.request=null;}
  private fieldScope(pending=false){return{entity:(pending?'analytical-request/':'analytical-fields/')+this.id()+(pending?'':'/'+this.periodId()),baseRevision:this.basis()?.reviewBasis??''};}
  private async loadBasis(){const id=this.id(),period=this.periodId(),comparison=this.comparisonPeriodId(),g=++this.basisGeneration;
    this.basis.set(null);this.preview.set(null);this.reviewed.set(false);this.request=null;
    if(!id||!period){this.message.set('Choose a reporting period to continue.');return;}
    const query=new URLSearchParams({periodId:period});if(comparison)query.set('comparisonPeriodId',comparison);
    try{const s=await this.api.get(this.base()+'/state?'+query.toString(),decodeAnalyticalState);if(g!==this.basisGeneration||id!==this.id())return;
      if(s.engagementId!==id||s.periodId!==period||s.comparisonPeriodId!==(comparison||null))throw new Error('Wrong analytical context');
      this.basis.set(s);this.draftAvailable.set(this.drafts.read(this.fieldScope(),analyticalEditableFields).state==='ready');
      const p=this.drafts.readPendingRequest(this.fieldScope(true));if(p.state==='ready'){this.pending.set(p.draft.value);this.uncertain.set(true);this.message.set(UNKNOWN_OUTCOME);}
      else if(!this.uncertain())this.message.set('');
    }catch(e){if(g===this.basisGeneration)this.message.set(e instanceof Error?e.message:'The reporting-period context is unavailable.');}}
  periodChanged(raw:string){if(!this.periods().some(p=>p.id===raw))return;this.periodId.set(raw);this.comparisonPeriodId.set('');void this.loadBasis();}
  comparisonChanged(raw:string){if(raw&&!this.comparisonPeriods().some(p=>p.id===raw))return;this.comparisonPeriodId.set(raw);void this.loadBasis();}
  setField(key:keyof AnalyticalEditableFields,value:string){this.model.update(m=>({...m,[key]:value}));this.preview.set(null);this.reviewed.set(false);this.request=null;}
  async prepare(){const s=this.basis(),g=this.requestGeneration;if(!s?.canPrepare||!this.valid()||this.busy()||(this.uncertain()&&!this.absent()))return;
    const prior=this.pending(),m={...this.model()},request:Intent={requestId:prior?.requestId??crypto.randomUUID(),reviewBasis:s.reviewBasis,
      fields:{...m,periodId:this.periodId(),comparisonPeriodId:this.comparisonPeriodId()||null},reason:m.reason.trim(),evidenceReference:m.evidenceReference.trim(),reviewed:false};
    this.busy.set(true);this.preview.set(null);this.reviewed.set(false);
    try{const outcome=await this.api.command(this.base()+'/preview',request);if(g!==this.requestGeneration)return;if(!outcome.ok){this.message.set(outcome.message);return;}
      const p=decodeAnalyticalPreview(outcome.value,'preview');if(JSON.stringify(request.fields)!==JSON.stringify({...this.model(),periodId:this.periodId(),comparisonPeriodId:this.comparisonPeriodId()||null})||
        p.engagementId!==this.id()||p.periodId!==s.periodId||p.comparisonPeriodId!==s.comparisonPeriodId||p.reviewBasis!==s.reviewBasis||p.currency!==s.currency||
        this.basis()?.reviewBasis!==s.reviewBasis||(prior&&prior.requestHash!==p.requestHash))throw new Error('Changed analytical preview');
      this.request=request;this.preview.set(p);this.message.set('');
    }catch{if(g===this.requestGeneration)this.message.set('The calculation preview could not be verified. Refresh the period context and review it again.');}
    finally{if(g===this.requestGeneration)this.busy.set(false);}}
  private matches(r:ReturnType<typeof decodeAnalyticalReceipt>,p:PendingRequestReference){return r.actorId===this.session.current()?.userId&&r.requestId===p.requestId&&r.requestHash===p.requestHash;}
  private clearPending(){this.drafts.clear(this.fieldScope(true).entity);this.pending.set(null);this.uncertain.set(false);this.absent.set(false);this.request=null;}
  async execute(){const request=this.request,p=this.preview(),s=this.basis(),g=this.requestGeneration;
    if(!request||!p?.canProceed||!this.reviewed()||this.busy()||!s||s.reviewBasis!==request.reviewBasis||!s.canPrepare||
      JSON.stringify(request.fields)!==JSON.stringify({...this.model(),periodId:this.periodId(),comparisonPeriodId:this.comparisonPeriodId()||null}))return;
    const reference={requestId:request.requestId,requestHash:p.requestHash};
    if(!this.drafts.save(this.fieldScope(),this.model(),analyticalEditableFields,true)||!this.drafts.save(this.fieldScope(true),reference,pendingRequestReference,true)){
      this.message.set('The recovery reference could not be saved in this tab. No preparation was sent.');return;}
    this.pending.set(reference);this.busy.set(true);this.reviewed.set(false);
    const outcome=await this.api.command(this.base(),{...request,reviewed:true});if(g!==this.requestGeneration)return;
    this.busy.set(false);this.preview.set(null);
    if(!outcome.ok){this.message.set(outcome.message);if(outcome.unknown){this.uncertain.set(true);this.absent.set(false);}else this.clearPending();return;}
    try{const r=decodeAnalyticalReceipt(outcome.value,'receipt');if(!this.matches(r,reference))throw new Error('Wrong receipt');
      this.receipt.set(r);this.clearPending();this.drafts.clear(this.fieldScope().entity);this.baseline.set(JSON.stringify(this.model()));
      this.message.set('Preparation retained. Independent review remains required; no conclusion or journal was created.');this.data.reload();
    }catch{this.uncertain.set(true);this.message.set(UNKNOWN_OUTCOME);}}
  async reconcile(){const p=this.pending(),g=this.requestGeneration;if(!p||!this.basis()||this.busy())return;this.busy.set(true);this.reviewed.set(false);this.preview.set(null);
    try{const out=await this.api.get(this.base()+'/receipts/'+p.requestId+'?requestHash='+p.requestHash,decodeAnalyticalLookup);if(g!==this.requestGeneration)return;
      if(out.found&&out.receipt&&this.matches(out.receipt,p)){this.receipt.set(out.receipt);this.absent.set(false);this.message.set('The retained receipt confirms the preparation. Acknowledge it to continue.');}
      else if(!out.found){this.absent.set(true);this.message.set('No receipt is retained. Restore the same fields, obtain a fresh preview and confirm before retrying this request.');}
      else throw new Error('Wrong receipt');
    }catch{if(g===this.requestGeneration)this.message.set('Receipt verification is unavailable. Keep this request reference and check again.');}
    finally{if(g===this.requestGeneration)this.busy.set(false);}}
  acknowledge(){const r=this.receipt(),p=this.pending();if(!r||!p||!this.matches(r,p))return;this.clearPending();this.drafts.clear(this.fieldScope().entity);
    this.model.set(empty());this.baseline.set(JSON.stringify(this.model()));this.receipt.set(null);this.data.reload();}
  saveDraft(){const ok=!this.busy()&&!this.uncertain()&&!!this.basis()&&this.drafts.save(this.fieldScope(),this.model(),analyticalEditableFields);
    this.message.set(ok?'Editable fields saved in this tab. Confirmation is never saved.':'Draft unavailable.');return ok;}
  restoreDraft(){if(this.busy())return;const d=this.drafts.read(this.fieldScope(),analyticalEditableFields);if(d.state!=='ready'){this.message.set('The editable fields are stale or unavailable.');return;}
    this.model.set(d.draft.value);this.preview.set(null);this.reviewed.set(false);this.request=null;this.draftAvailable.set(false);}
  discardDraft(){if(this.busy()||this.uncertain())return false;this.model.set(JSON.parse(this.baseline()));this.preview.set(null);this.reviewed.set(false);this.request=null;this.drafts.clear(this.fieldScope().entity);return true;}
  async confirmNavigation(){if(this.busy()||this.uncertain()){this.message.set('Resolve the retained preparation receipt before leaving.');return false;}
    if(!this.dirty())return true;const answer=await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());return answer==='save'?this.saveDraft():answer==='discard'?this.discardDraft():false;}
  async refresh(){if(this.busy())return;if(!this.uncertain()&&this.dirty()&&!(await this.confirmNavigation()))return;this.preview.set(null);this.reviewed.set(false);this.data.reload();}
  @HostListener('window:beforeunload',['$event']) beforeUnload(e:BeforeUnloadEvent){if(this.busy()||this.uncertain()||this.dirty()){e.preventDefault();e.returnValue='';}}
}
