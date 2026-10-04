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
import { decodeSpecialistContext, decodeSpecialistLookup, decodeSpecialistPreview, decodeSpecialistReceipt,
  decodeSpecialistState } from './analysis-preparation-contracts';

const commonAmounts=['openingAmount','additionsAmount','disposalsAmount','depreciationAmount','impairmentAmount','interestAmount','currentPortion','nonCurrentPortion','capitalMovement','dividends','taxPaid','managementAmount'] as const;
const optionalAmounts=['payrollGrossAmount','payrollDeductionsAmount','payrollNetAmount','loanRepaymentAmount','equityProfitOrLossAmount','equityOciAmount','taxBaseAmount','taxRate','forecastCashInputAmount','forecastDebtInputAmount'] as const;
const fields=['methodologyVersion','assumptionsHash',...commonAmounts,'depreciationMethod','usefulLifeMonths',...optionalAmounts,
  'payrollContractReference','payrollBankPaymentReference','loanMaturityDate','loanCovenantReference','relatedPartyDisclosureReference',
  'taxJurisdiction','taxRuleVersion','taxReturnEvidenceReference','taxPaymentEvidenceReference','taxCorrespondenceReference',
  'forecastOwner','forecastHorizonEnd','forecastSensitivityReference','forecastSensitivityResult','evidenceReference','reason'] as const;
type Model=Record<typeof fields[number],string>;
const empty=():Model=>({methodologyVersion:'',assumptionsHash:'',openingAmount:'0',additionsAmount:'0',disposalsAmount:'0',depreciationAmount:'0',
  impairmentAmount:'0',interestAmount:'0',currentPortion:'0',nonCurrentPortion:'0',capitalMovement:'0',dividends:'0',taxPaid:'0',managementAmount:'0',
  depreciationMethod:'',usefulLifeMonths:'',payrollGrossAmount:'',payrollDeductionsAmount:'',payrollNetAmount:'',payrollContractReference:'',payrollBankPaymentReference:'',
  loanRepaymentAmount:'',loanMaturityDate:'',loanCovenantReference:'',equityProfitOrLossAmount:'',equityOciAmount:'',relatedPartyDisclosureReference:'',
  taxJurisdiction:'',taxRuleVersion:'',taxBaseAmount:'',taxRate:'',taxReturnEvidenceReference:'',taxPaymentEvidenceReference:'',taxCorrespondenceReference:'',
  forecastOwner:'',forecastHorizonEnd:'',forecastCashInputAmount:'',forecastDebtInputAmount:'',forecastSensitivityReference:'',forecastSensitivityResult:'',
  evidenceReference:'',reason:''});
export function specialistEditableFields(raw:unknown):Model|null{
  if(!raw||typeof raw!=='object'||Array.isArray(raw))return null;const x=raw as Record<string,unknown>;
  return Object.keys(x).length===fields.length&&fields.every(k=>typeof x[k]==='string'&&(x[k] as string).length<=4000)?x as Model:null;
}
type Intent={requestId:string;reviewBasis:string;supersedesScheduleId:string|null;model:Model;periodId:string;area:string};
const areaOptions=['ASSETS','PAYROLL','LOANS','EQUITY','RELATED_PARTIES','TAX','FORECAST'];
@Component({selector:'audit-specialist-preparation',imports:[RouterLink,MatButtonModule,...SHARED],templateUrl:'./specialist-preparation.html',
 styles:[`.panel{overflow-wrap:anywhere}label{display:block;margin-block:.7rem}input,select,textarea{max-width:100%;box-sizing:border-box}textarea{width:min(100%,48rem)}.actions{display:flex;gap:.75rem;flex-wrap:wrap}dt{font-weight:600;margin-block-start:.5rem}dd{margin-inline-start:0}.table-scroll{overflow-x:auto}`]})
export class SpecialistPreparation implements NavigationProtected{
 private readonly api=inject(Api);private readonly session=inject(SessionService);private readonly drafts=inject(TabDrafts);private readonly dialog=inject(MatDialog);private readonly route=inject(ActivatedRoute);private readonly router=inject(Router);
 readonly id=routeGuid();readonly context=this.api.resource(()=>this.id()?`/api/ui/engagements/${this.id()}/specialist-preparation`:null,decodeSpecialistContext);
 readonly periodId=signal('');readonly area=signal('ASSETS');readonly model=signal(empty());
 readonly state=this.api.resource(()=>this.id()&&this.periodId()&&this.area()?`/api/ui/engagements/${this.id()}/specialist-preparation/state?periodId=${this.periodId()}&area=${this.area()}`:null,decodeSpecialistState);
 readonly valid=computed(()=>{const m=this.model(),area=this.area(),s=this.state.data();const amount=(x:string)=>/^-?\d{1,13}(?:\.\d{1,6})?$/.test(x);
  return!!s?.canPrepare&&/^[a-f0-9]{64}$/i.test(m.assumptionsHash)&&!!m.methodologyVersion.trim()&&m.methodologyVersion.length<=100&&!!m.evidenceReference.trim()&&
    !!m.reason.trim()&&m.reason.length<=4000&&commonAmounts.every(k=>amount(m[k]))&&
    (area!=='ASSETS'||!!m.depreciationMethod.trim()&&/^\d{1,5}$/.test(m.usefulLifeMonths))&&
    (area!=='PAYROLL'||[m.payrollGrossAmount,m.payrollDeductionsAmount,m.payrollNetAmount].every(amount)&&!!m.payrollContractReference.trim()&&!!m.payrollBankPaymentReference.trim())&&
    (area!=='LOANS'||amount(m.loanRepaymentAmount)&&!!m.loanMaturityDate&&!!m.loanCovenantReference.trim())&&
    (area!=='EQUITY'||amount(m.equityProfitOrLossAmount)&&amount(m.equityOciAmount)&&!!m.relatedPartyDisclosureReference.trim())&&
    (area!=='RELATED_PARTIES'||!!m.relatedPartyDisclosureReference.trim())&&
    (area!=='TAX'||!!m.taxJurisdiction.trim()&&!!m.taxRuleVersion.trim()&&amount(m.taxBaseAmount)&&amount(m.taxRate)&&!!m.taxReturnEvidenceReference.trim()&&!!m.taxPaymentEvidenceReference.trim()&&!!m.taxCorrespondenceReference.trim())&&
    (area!=='FORECAST'||!!m.forecastOwner.trim()&&!!m.forecastHorizonEnd&&amount(m.forecastCashInputAmount)&&amount(m.forecastDebtInputAmount)&&!!m.forecastSensitivityReference.trim()&&!!m.forecastSensitivityResult.trim());});
 readonly preview=signal<ReturnType<typeof decodeSpecialistPreview>|null>(null);readonly receipt=signal<ReturnType<typeof decodeSpecialistReceipt>|null>(null);readonly pending=signal<PendingRequestReference|null>(null);
 readonly busy=signal(false);readonly reviewed=signal(false);readonly uncertain=signal(false);readonly absent=signal(false);readonly message=signal('');readonly draftAvailable=signal(false);
 private request:Intent|null=null;private generation=0;private baseline=signal(JSON.stringify(empty()));readonly dirty=computed(()=>JSON.stringify(this.model())!==this.baseline());
 readonly areas=areaOptions;readonly commonAmounts=commonAmounts;
 setField(key:keyof Model,value:string){this.model.update(m=>({...m,[key]:value}));}
 constructor(){effect(()=>{this.id();this.session.invalidation();untracked(()=>{this.generation++;this.periodId.set('');this.area.set('ASSETS');this.model.set(empty());this.baseline.set(JSON.stringify(this.model()));
  this.preview.set(null);this.receipt.set(null);this.pending.set(null);this.request=null;this.busy.set(false);this.reviewed.set(false);this.uncertain.set(false);this.absent.set(false);this.message.set('');this.draftAvailable.set(false);});});
  effect(()=>{const c=this.context.data();if(!c)return;untracked(()=>{if(!this.periodId()){const query=this.route.snapshot.queryParamMap.get('periodId');this.periodId.set(c.periods.some(x=>x.id===query)?query!:c.periods[0]?.id??'');const area=this.route.snapshot.queryParamMap.get('area');if(areaOptions.includes(area??''))this.area.set(area!);}});});
  effect(()=>{const s=this.state.data();if(!s)return;untracked(()=>{this.preview.set(null);this.reviewed.set(false);this.draftAvailable.set(this.drafts.read(this.scope(),specialistEditableFields).state==='ready');
   const p=this.drafts.readPendingRequest(this.scope(true));if(p.state==='ready'){this.pending.set(p.draft.value);this.uncertain.set(true);this.message.set(UNKNOWN_OUTCOME);}});});
  effect(()=>{this.model();this.periodId();this.area();untracked(()=>{this.preview.set(null);this.reviewed.set(false);});});}
 private updateUrl(){this.router.navigate([],{relativeTo:this.route,queryParams:{periodId:this.periodId(),area:this.area()},queryParamsHandling:'merge',replaceUrl:true});}
 changePeriod(id:string){if(this.busy()||this.uncertain())return;this.periodId.set(id);this.model.set(empty());this.updateUrl();}
 changeArea(area:string){if(this.busy()||this.uncertain()||!areaOptions.includes(area))return;this.area.set(area);this.model.set(empty());this.updateUrl();}
 private scope(pending=false){return{entity:`${pending?'specialist-request':'specialist-fields'}/${this.id()}/${this.periodId()}/${this.area()}`,baseRevision:this.state.data()?.reviewBasis??''};}
 private makeRequest(retained=this.pending()):Intent|null{const s=this.state.data();return s?{requestId:retained?.requestId??crypto.randomUUID(),reviewBasis:s.reviewBasis,supersedesScheduleId:s.currentScheduleId,model:{...this.model()},periodId:this.periodId(),area:this.area()}:null;}
 private body(r:Intent,reviewed=false){const m=r.model,area=r.area;
  const nullable=(key:keyof Model)=>m[key]||null;
  const fields={clientId:this.state.data()?.clientId,engagementId:this.id(),periodId:r.periodId,area,methodologyVersion:m.methodologyVersion.trim(),
   openingAmount:m.openingAmount,additionsAmount:m.additionsAmount,disposalsAmount:m.disposalsAmount,depreciationAmount:m.depreciationAmount,impairmentAmount:m.impairmentAmount,
   interestAmount:m.interestAmount,currentPortion:m.currentPortion,nonCurrentPortion:m.nonCurrentPortion,capitalMovement:m.capitalMovement,dividends:m.dividends,taxPaid:m.taxPaid,
   managementAmount:m.managementAmount,assumptionsHash:m.assumptionsHash.toLowerCase(),evidenceReference:m.evidenceReference.trim(),
   depreciationMethod:area==='ASSETS'?nullable('depreciationMethod'):null,usefulLifeMonths:area==='ASSETS'?Number(m.usefulLifeMonths):null,
   payrollGrossAmount:area==='PAYROLL'?m.payrollGrossAmount:null,payrollDeductionsAmount:area==='PAYROLL'?m.payrollDeductionsAmount:null,payrollNetAmount:area==='PAYROLL'?m.payrollNetAmount:null,
   payrollContractReference:area==='PAYROLL'?m.payrollContractReference.trim():null,payrollBankPaymentReference:area==='PAYROLL'?m.payrollBankPaymentReference.trim():null,
   loanRepaymentAmount:area==='LOANS'?m.loanRepaymentAmount:null,loanMaturityDate:area==='LOANS'?m.loanMaturityDate:null,loanCovenantReference:area==='LOANS'?m.loanCovenantReference.trim():null,
   equityProfitOrLossAmount:area==='EQUITY'?m.equityProfitOrLossAmount:null,equityOciAmount:area==='EQUITY'?m.equityOciAmount:null,
   relatedPartyDisclosureReference:area==='EQUITY'||area==='RELATED_PARTIES'?m.relatedPartyDisclosureReference.trim():null,
   taxJurisdiction:area==='TAX'?m.taxJurisdiction.trim():null,taxRuleVersion:area==='TAX'?m.taxRuleVersion.trim():null,taxBaseAmount:area==='TAX'?m.taxBaseAmount:null,taxRate:area==='TAX'?m.taxRate:null,
   taxReturnEvidenceReference:area==='TAX'?m.taxReturnEvidenceReference.trim():null,taxPaymentEvidenceReference:area==='TAX'?m.taxPaymentEvidenceReference.trim():null,taxCorrespondenceReference:area==='TAX'?m.taxCorrespondenceReference.trim():null,
   forecastOwner:area==='FORECAST'?m.forecastOwner.trim():null,forecastHorizonEnd:area==='FORECAST'?m.forecastHorizonEnd:null,forecastCashInputAmount:area==='FORECAST'?m.forecastCashInputAmount:null,
   forecastDebtInputAmount:area==='FORECAST'?m.forecastDebtInputAmount:null,forecastSensitivityReference:area==='FORECAST'?m.forecastSensitivityReference.trim():null,
   forecastSensitivityResult:area==='FORECAST'?m.forecastSensitivityResult.trim():null};
  return{requestId:r.requestId,reviewBasis:r.reviewBasis,supersedesScheduleId:r.supersedesScheduleId,reason:m.reason.trim(),reviewed,fields};}
 async prepare(){const s=this.state.data(),g=this.generation;if(!s?.canPrepare||!this.valid()||this.busy()||(this.uncertain()&&!this.absent()))return;const r=this.makeRequest();if(!r)return;
  this.busy.set(true);this.preview.set(null);this.reviewed.set(false);try{const out=await this.api.command(`/api/ui/engagements/${this.id()}/specialist-preparation/preview`,this.body(r));if(g!==this.generation)return;
   if(!out.ok){this.message.set(out.message);return;}const p=decodeSpecialistPreview(out.value,'preview');if(p.engagementId!==this.id()||p.periodId!==r.periodId||p.area!==r.area||p.reviewBasis!==s.reviewBasis||this.state.data()?.reviewBasis!==s.reviewBasis||
    (this.pending()&&this.pending()!.requestHash!==p.requestHash))throw new Error('Changed preview');this.request=r;this.preview.set(p);this.message.set('');}
  catch{if(g===this.generation)this.message.set('The specialist calculation preview could not be verified. Refresh the period and revision.');}finally{if(g===this.generation)this.busy.set(false);}}
 private matches(r:ReturnType<typeof decodeSpecialistReceipt>,p:PendingRequestReference){return r.scheduleId===r.result.scheduleId&&r.actorId===this.session.current()?.userId&&r.requestId===p.requestId&&r.requestHash===p.requestHash;}
 private clearPending(){this.drafts.clear(this.scope(true).entity);this.pending.set(null);this.uncertain.set(false);this.absent.set(false);this.request=null;}
 async execute(){const r=this.request,p=this.preview(),g=this.generation;if(!r||!p?.canProceed||!this.reviewed()||this.busy()||this.state.data()?.reviewBasis!==r.reviewBasis||JSON.stringify(r.model)!==JSON.stringify(this.model()))return;
  const ref={requestId:r.requestId,requestHash:p.requestHash};if(!this.drafts.save(this.scope(),this.model(),specialistEditableFields,true)||!this.drafts.save(this.scope(true),ref,pendingRequestReference,true)){this.message.set('This tab could not save a recovery reference. No schedule was sent.');return;}
  this.pending.set(ref);this.busy.set(true);this.reviewed.set(false);const out=await this.api.command(`/api/ui/engagements/${this.id()}/specialist-preparation`,this.body(r,true));if(g!==this.generation)return;this.busy.set(false);this.preview.set(null);
  if(!out.ok){this.message.set(out.message);if(out.unknown){this.uncertain.set(true);this.absent.set(false);}else this.clearPending();return;}
  try{const saved=decodeSpecialistReceipt(out.value,'receipt');if(!this.matches(saved,ref))throw new Error('Wrong receipt');this.receipt.set(saved);this.clearPending();this.drafts.clear(this.scope().entity);this.baseline.set(JSON.stringify(empty()));
   this.message.set('The specialist schedule revision and immutable receipt are retained. Independent review remains separate.');this.context.reload();}
  catch{this.uncertain.set(true);this.message.set(UNKNOWN_OUTCOME);}}
 async reconcile(){const p=this.pending(),g=this.generation;if(!p||!this.state.data()||this.busy())return;this.busy.set(true);this.reviewed.set(false);this.preview.set(null);
  try{const v=await this.api.get(`/api/ui/engagements/${this.id()}/specialist-preparation/receipts/${p.requestId}?requestHash=${p.requestHash}`,decodeSpecialistLookup);if(g!==this.generation)return;
   if(v.found&&v.receipt&&this.matches(v.receipt,p)){this.receipt.set(v.receipt);this.absent.set(false);this.message.set('The retained receipt confirms this schedule revision. Acknowledge it before continuing.');}
   else if(!v.found){this.absent.set(true);this.message.set('No receipt is retained. Restore the identical fields and obtain a fresh preview and assent before retrying.');}else throw new Error('Wrong receipt');}
  catch{if(g===this.generation)this.message.set('Receipt verification is unavailable. Keep the request reference and check again.');}finally{if(g===this.generation)this.busy.set(false);}}
 acknowledge(){const r=this.receipt(),p=this.pending();if(!r||!p||!this.matches(r,p))return;this.clearPending();this.drafts.clear(this.scope().entity);this.model.set(empty());this.baseline.set(JSON.stringify(this.model()));this.receipt.set(null);this.context.reload();}
 saveDraft(){const ok=!this.busy()&&!this.uncertain()&&this.drafts.save(this.scope(),this.model(),specialistEditableFields);this.message.set(ok?'Editable fields saved in this tab; assent is excluded.':'Draft unavailable.');return ok;}
 restoreDraft(){if(this.busy())return;const d=this.drafts.read(this.scope(),specialistEditableFields);if(d.state!=='ready'){this.message.set('The editable fields are stale or unavailable.');return;}this.model.set(d.draft.value);this.preview.set(null);this.reviewed.set(false);this.draftAvailable.set(false);}
 async confirmNavigation(){if(this.busy()||this.uncertain()){this.message.set('Resolve the retained schedule receipt before leaving.');return false;}if(!this.dirty())return true;
  const result=await firstValueFrom(this.dialog.open(UnsavedChangesDialog).afterClosed());return result==='save'?this.saveDraft():result==='discard'?this.discardDraft():false;}
 discardDraft(){if(this.busy()||this.uncertain())return false;this.model.set(empty());this.baseline.set(JSON.stringify(this.model()));this.preview.set(null);this.reviewed.set(false);this.drafts.clear(this.scope().entity);return true;}
 async refresh(){if(this.busy())return;if(!this.uncertain()&&this.dirty()&&!(await this.confirmNavigation()))return;this.preview.set(null);this.reviewed.set(false);this.state.reload();this.context.reload();}
 @HostListener('window:beforeunload',['$event']) beforeUnload(e:BeforeUnloadEvent){if(this.busy()||this.uncertain()||this.dirty()){e.preventDefault();e.returnValue='';}}
}
