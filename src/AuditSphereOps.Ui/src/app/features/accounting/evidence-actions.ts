import { afterNextRender, Component, DestroyRef, ElementRef, HostListener, Injector, computed, effect, inject, signal, untracked, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { form, FormField, maxLength, required } from '@angular/forms/signals';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogRef } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { Api, routeGuid, UNKNOWN_OUTCOME } from '../../core/api';
import { SessionService } from '../../core/session';
import { PendingRequestReference, pendingRequestReference, TabDrafts } from '../../core/tab-drafts';
import { NavigationProtected, UnsavedChangesDialog } from '../../core/unsaved-changes';
import { SHARED } from '../../core/ui';
import { decodeEvidenceActions, decodeEvidenceLookup, decodeEvidencePreview, decodeEvidenceProcedures, decodeEvidenceReceipt,
  EvidenceActionFields, evidenceActionFields } from './evidence-action-contracts';
import { decodeAnalysisReview } from './analysis-review';

const empty = (): EvidenceActionFields => ({ action: 'LINK', resultId: null, resultBasis: null, decision: '', reason: '', evidenceReference: '' });
type Request = EvidenceActionFields & { requestId: string; reviewBasis: string; reviewed: boolean };
type EvidenceFocusOrigin = { element: Element; interaction: number };
@Component({ selector: 'audit-evidence-actions', imports: [RouterLink, FormField, MatButtonModule, ...SHARED],
  templateUrl: './evidence-actions.html', styles: [`.panel { overflow-wrap: anywhere; } label { display: block; margin-block: .75rem; } textarea { width: 100%; box-sizing: border-box; }`] })
export class EvidenceActions implements NavigationProtected {
  private readonly api = inject(Api); private readonly session = inject(SessionService); private readonly drafts = inject(TabDrafts); private readonly dialog = inject(MatDialog); private readonly destroyRef=inject(DestroyRef);
  private readonly hostElement = inject<ElementRef<HTMLElement>>(ElementRef); private readonly renderInjector = inject(Injector);
  private readonly reviewHeading = viewChild<ElementRef<HTMLElement>>('reviewHeading');
  private readonly previewTrigger = viewChild<unknown,ElementRef<HTMLButtonElement>>('previewTrigger',{read:ElementRef});
  private readonly route = inject(ActivatedRoute); private readonly params = toSignal(this.route.paramMap, { initialValue: this.route.snapshot?.paramMap });
  readonly id = routeGuid(); readonly kind = computed(() => { const k = this.params()?.get('kind') ?? ''; return ['ECL','INVENTORY','SPECIALIST','ANALYTICAL','JOURNAL_RISK'].includes(k) ? k : null; });
  readonly historyPage = signal(0); readonly procedurePage = signal(0); readonly model = signal<EvidenceActionFields>(empty());
  readonly action = computed(() => this.model().action);
  readonly fields = form(this.model, p => { required(p.reason); maxLength(p.reason,4000); required(p.evidenceReference); maxLength(p.evidenceReference,2000); });
  readonly detail = this.api.resource(() => this.base() ? this.base() + '?page=0' : null, (raw,path) => {
    const v = decodeAnalysisReview(raw,path); if(v.kind!==this.kind() || v.id!==this.id() || v.page!==0) throw new Error('Wrong evidence target'); return v;
  });
  readonly state = this.api.resource(() => this.base() ? this.base() + '/actions?page=' + this.historyPage() : null, (raw,path) => {
    const v = decodeEvidenceActions(raw,path); if(v.kind!==this.kind() || v.evidenceId!==this.id() || v.page!==this.historyPage()) throw new Error('Wrong action target'); return v;
  });
  readonly context = computed(() => { const v=this.detail.data(), s=this.state.data();
    if(!v || !s || v.reviewBasis!==s.reviewBasis || (s.canLink && v.reviewedAt!==null) ||
      (s.canReview && (v.reviewedAt!==null || v.createdByUserId===null || v.createdByUserId===this.session.current()?.userId)) ||
      (s.decisions.includes('APPROVED') && (!v.inputsCurrent || !v.hasCurrentReviewedProcedure))) return null;
    return {v,s}; });
  readonly procedures = this.api.resource(() => this.context()?.s.canLink && this.action()==='LINK' ? this.base() + '/procedure-results?page=' + this.procedurePage() : null,
    (raw,path) => {const v=decodeEvidenceProcedures(raw,path); if(v.kind!==this.kind() || v.evidenceId!==this.id() || v.page!==this.procedurePage() || v.reviewBasis!==this.context()?.s.reviewBasis) throw new Error('Wrong result context');return v;});
  readonly preview=signal<ReturnType<typeof decodeEvidencePreview>|null>(null); readonly receipt=signal<ReturnType<typeof decodeEvidenceReceipt>|null>(null);
  readonly reviewed=signal(false); readonly busy=signal(false); readonly uncertain=signal(false); readonly message=signal(''); readonly announcement=signal(''); readonly draftAvailable=signal(false);
  readonly pending=signal<PendingRequestReference|null>(null); readonly dirty=computed(()=>JSON.stringify(this.model())!==this.baseline());
  private readonly baseline=signal(JSON.stringify(empty())); private generation=0; private destroyed=false; private focusInteraction=0; private navigationDialog:MatDialogRef<UnsavedChangesDialog>|null=null; private request:Request|null=null; readonly absent=signal(false);
  constructor() {
    this.destroyRef.onDestroy(()=>{this.destroyed=true;this.invalidateOwner();});
    effect(()=>{this.id();this.kind();this.session.invalidation();untracked(()=>{this.invalidateOwner();this.historyPage.set(0);this.procedurePage.set(0);this.model.set(empty());this.baseline.set(JSON.stringify(empty()));
      this.preview.set(null);this.receipt.set(null);this.reviewed.set(false);this.busy.set(false);this.uncertain.set(false);this.pending.set(null);this.message.set('');this.announcement.set('');this.request=null;this.absent.set(false);this.draftAvailable.set(false);});});
    effect(()=>{const c=this.context();if(!c)return;untracked(()=>{this.preview.set(null);this.reviewed.set(false);this.draftAvailable.set(this.drafts.read(this.scope(),evidenceActionFields).state==='ready');
      const p=this.drafts.readPendingRequest(this.scope(true));if(p.state==='ready'){this.pending.set(p.draft.value);this.uncertain.set(true);this.message.set(UNKNOWN_OUTCOME);}});});
    effect(()=>{this.model();untracked(()=>{const hadPreview=this.preview()!==null;this.preview.set(null);this.reviewed.set(false);
      if(hadPreview)this.announcement.set('The exact action changed. Prepare a new preview and provide fresh assent.');});});
  }
  private invalidateOwner(){this.generation++;const dialog=this.navigationDialog;this.navigationDialog=null;dialog?.close();}
  @HostListener('document:pointerdown')
  @HostListener('document:keydown')
  recordFocusInteraction():void{this.focusInteraction++;}
  private focusOwner(){const s=this.session.current();return `${this.kind()}:${this.id()}:${s?.firmId??''}:${s?.userId??''}:${s?.generation??''}:${this.session.invalidation()}:${this.generation}`;}
  private captureOwner():()=>boolean{const owner=this.focusOwner(),actor=this.session.current();return ()=>!this.destroyed&&actor===this.session.current()&&owner===this.focusOwner();}
  private focusedControl():EvidenceFocusOrigin|null{const host=this.hostElement.nativeElement,active=host.ownerDocument.activeElement;
    return active&&host.contains(active)?{element:active,interaction:this.focusInteraction}:null;}
  private focusWhenCurrent(target:()=>ElementRef<HTMLElement>|undefined,owner:string,origin:EvidenceFocusOrigin,current:()=>boolean):void{
    afterNextRender(()=>{const document=this.hostElement.nativeElement.ownerDocument;
      if(this.destroyed||owner!==this.focusOwner()||origin.interaction!==this.focusInteraction||
        (document.activeElement!==origin.element&&document.activeElement!==document.body)||!current())return;
      target()?.nativeElement.focus();},{injector:this.renderInjector});
  }
  private matchesIntent(r:Request):boolean{const m=this.model();return m.action===r.action&&m.resultId===r.resultId&&m.resultBasis===r.resultBasis&&
    m.decision===r.decision&&m.reason===r.reason&&m.evidenceReference===r.evidenceReference;}
  private base(){return this.kind() && this.id() ? '/api/ui/accounting/evidence/'+this.kind()+'/'+this.id() : null;}
  private scope(pending=false){return {entity:(pending?'evidence-request/':'evidence-fields/')+this.kind()+'/'+this.id(),baseRevision:this.context()?.s.reviewBasis??''};}
  changeAction(event:Event){if(this.busy()||(this.uncertain()&&!this.absent()))return;const action=(event.target as HTMLSelectElement).value;if(action!=='LINK'&&action!=='REVIEW')return;this.model.update(m=>({...m,action,resultId:null,resultBasis:null,decision:''}));}
  chooseResult(event:Event){if(this.busy()||(this.uncertain()&&!this.absent()))return;const id=(event.target as HTMLSelectElement).value;const r=this.procedures.data()?.rows.find(x=>x.resultId===id&&!x.alreadyLinked);
    this.model.update(m=>({...m,resultId:r?.resultId??null,resultBasis:r?.resultBasis??null}));}
  chooseDecision(event:Event){if(!this.busy()&&(!this.uncertain()||this.absent())){const target=event.target;if(!(target instanceof HTMLSelectElement))return;const decision=this.context()?.s.decisions.find(candidate=>candidate===target.value);if(decision!==undefined)this.model.update(m=>({...m,decision}));}}
  async prepare(){const c=this.context(),current=this.captureOwner(),m=this.model();if(!c||this.busy()||!this.fields().valid()||(this.uncertain()&&!this.absent())||
    (m.action==='LINK' ? !c.s.canLink||!m.resultId : !c.s.canReview||!m.decision))return;
    const origin=this.focusedControl(),owner=this.focusOwner(),pending=this.pending(),r:Request={...structuredClone(m),requestId:pending?.requestId??crypto.randomUUID(),reviewBasis:c.s.reviewBasis,reviewed:false};
    this.busy.set(true);this.preview.set(null);this.reviewed.set(false);this.message.set('');this.announcement.set('Preparing the exact action review preview.');try{const result=await this.api.command(this.base()+'/preview',r);if(!current())return;
      if(!result.ok){this.message.set(result.message);this.announcement.set('');return;}const p=decodeEvidencePreview(result.value,"preview");if(p.kind!==this.kind()||p.evidenceId!==this.id()||p.action!==r.action||p.reviewBasis!==r.reviewBasis||
        this.context()?.s.reviewBasis!==r.reviewBasis||(pending&&pending.requestHash!==p.requestHash))throw new Error('The exact reviewed intent changed. Refresh or verify its retained receipt.');
      this.request=r;this.preview.set(p);this.message.set('');this.announcement.set('Exact action review preview ready. Review the exact source, result revision and human decision; fresh assent is required before recording.');
      if(origin)this.focusWhenCurrent(()=>this.reviewHeading(),owner,origin,()=>current()&&this.request===r&&this.preview()===p&&this.matchesIntent(r)&&this.context()?.s.reviewBasis===r.reviewBasis);
    }catch(e){if(current()){this.message.set(e instanceof Error?e.message:'Preview unavailable.');this.announcement.set('');}}finally{if(current())this.busy.set(false);}}
  cancelPreview(){const p=this.preview(),origin=this.focusedControl(),owner=this.focusOwner(),current=this.captureOwner();if(!p||this.busy())return;
    this.preview.set(null);this.reviewed.set(false);this.request=null;this.message.set('');
    this.announcement.set('Review preview cancelled. Editable fields remain; a fresh preview and assent are required.');
    if(origin)this.focusWhenCurrent(()=>this.previewTrigger(),owner,origin,()=>current()&&this.preview()===null&&this.request===null);
  }
  private matches(r:ReturnType<typeof decodeEvidenceReceipt>,p:PendingRequestReference){return r.kind===this.kind()&&r.evidenceId===this.id()&&r.actorId===this.session.current()?.userId&&r.requestId===p.requestId&&r.requestHash===p.requestHash;}
  private clearPending(){this.drafts.clear(this.scope(true).entity);this.pending.set(null);this.uncertain.set(false);this.absent.set(false);this.request=null;}
  async execute(){const p=this.preview(),r=this.request,c=this.context(),current=this.captureOwner();if(!c||!p?.canProceed||!r||!this.reviewed()||this.busy()||c.s.reviewBasis!==r.reviewBasis||
    JSON.stringify(this.model())!==JSON.stringify(({action:r.action,resultId:r.resultId,resultBasis:r.resultBasis,decision:r.decision,reason:r.reason,evidenceReference:r.evidenceReference})))return;
    const ref={requestId:r.requestId,requestHash:p.requestHash};
    // If storage is unavailable, refuse dispatch; the retained request reference is required for recovery.
    if(!this.drafts.save(this.scope(),this.model(),evidenceActionFields,true)||!this.drafts.save(this.scope(true),ref,pendingRequestReference,true)){this.message.set('The recovery reference could not be retained in this tab. No action was sent.');return;}
    this.announcement.set('');
    this.pending.set(ref);this.busy.set(true);this.reviewed.set(false);const result=await this.api.command(this.base()+'/actions',{...r,reviewed:true});if(!current())return;this.busy.set(false);this.preview.set(null);
    if(!result.ok){this.message.set(result.message);if(result.unknown){this.uncertain.set(true);this.absent.set(false);}else this.clearPending();return;}
    try{const receipt=decodeEvidenceReceipt(result.value);if(!this.matches(receipt,ref))throw new Error('Wrong receipt');this.receipt.set(receipt);this.clearPending();this.drafts.clear(this.scope().entity);this.baseline.set(JSON.stringify(this.model()));
      this.message.set('The reviewed action and immutable evidence are retained.');this.detail.reload();this.state.reload();}catch{this.uncertain.set(true);this.message.set(UNKNOWN_OUTCOME);}}
  async reconcile(){const p=this.pending(),current=this.captureOwner();if(!p||!this.context()||this.busy())return;this.busy.set(true);this.preview.set(null);this.reviewed.set(false);this.announcement.set('');
    try{const l=await this.api.get(this.base()+'/receipts/'+p.requestId+'?requestHash='+p.requestHash,decodeEvidenceLookup);if(!current())return;
      if(l.found&&l.receipt&&this.matches(l.receipt,p)){this.receipt.set(l.receipt);this.absent.set(false);this.message.set('The retained receipt confirms this action. Acknowledge it before continuing.');}
      else if(!l.found){this.absent.set(true);this.message.set('No receipt is currently retained. Restore the identical original fields and obtain fresh preview and assent before retrying the same request. A changed intent cannot reuse it.');}
      else throw new Error('Wrong receipt');}catch{if(current())this.message.set('Receipt verification is unavailable. Keep the reference and check again.');}finally{if(current())this.busy.set(false);}}
  acknowledge(){const r=this.receipt(),p=this.pending();if(!r||!p||!this.matches(r,p))return;this.clearPending();this.drafts.clear(this.scope().entity);this.model.set(empty());this.baseline.set(JSON.stringify(empty()));this.detail.reload();this.state.reload();}
  saveDraft(){const ok=!this.busy()&&!this.uncertain()&&this.drafts.save(this.scope(),this.model(),evidenceActionFields);this.message.set(ok?'Editable fields saved in this tab; assent is excluded.':'Draft unavailable.');return ok;}
  restoreDraft(){if(this.busy())return;const d=this.drafts.read(this.scope(),evidenceActionFields);if(d.state!=='ready'){this.message.set('The saved fields are stale or unavailable.');return;}this.announcement.set('');this.model.set(d.draft.value);this.draftAvailable.set(false);this.preview.set(null);this.reviewed.set(false);}
  discardDraft(){if(this.busy()||this.uncertain())return false;this.announcement.set('');this.model.set(JSON.parse(this.baseline()));this.preview.set(null);this.reviewed.set(false);this.drafts.clear(this.scope().entity);return true;}
  async confirmNavigation(){if(this.busy()||this.uncertain()){this.message.set('Resolve the retained request receipt before leaving.');return false;}if(!this.dirty())return true;
    const current=this.captureOwner();const dialog=this.dialog.open(UnsavedChangesDialog);this.navigationDialog=dialog;const d=await firstValueFrom(dialog.afterClosed());if(this.navigationDialog===dialog)this.navigationDialog=null;
    if(!current())return false;return d==='save'?this.saveDraft():d==='discard'?this.discardDraft():false;}
  @HostListener('window:beforeunload',['$event']) beforeUnload(e:BeforeUnloadEvent){if(this.busy()||this.uncertain()||this.dirty()){e.preventDefault();e.returnValue='';}}
  async refresh(){const current=this.captureOwner();if(this.busy())return;if(!this.uncertain()&&this.dirty()&&(!(await this.confirmNavigation())||!current()))return;
    if(!current())return;this.preview.set(null);this.reviewed.set(false);this.announcement.set('');this.detail.reload();this.state.reload();}
}
