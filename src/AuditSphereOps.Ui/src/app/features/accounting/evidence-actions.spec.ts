import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { SessionService } from '../../core/session';
import { EvidenceActions } from './evidence-actions';
import { decodeEvidenceActions, decodeEvidenceLookup, decodeEvidenceReceipt, evidenceActionFields } from './evidence-action-contracts';

const id='11111111-1111-4111-8111-111111111111',actor='22222222-2222-4222-8222-222222222222',resultId='33333333-3333-4333-8333-333333333333',root='/api/ui/accounting/evidence/ECL/'+id;
const view={id,kind:'ECL',clientId:id,clientName:'Synthetic client',engagementId:id,engagementName:'Scoped audit',periodId:id,periodCode:'FY26',basis:'IFRS',currency:'QAR',area:'CASH',status:'PREPARED',version:1,
  inputGeneration:1,currentGeneration:1,inputsCurrent:true,source:{kind:'TRIAL_BALANCE',id,reconciliationId:id,retainedDigest:'b'.repeat(64),currentDigest:'b'.repeat(64),currentChecksPass:true,description:'Exact source.'},
  assumptionsDigest:'c'.repeat(64),replayDigest:null,replayMatchesInputs:null,proposedJournalId:null,createdByUserId:id,createdAt:'2026-10-03T00:00:00Z',reviewedByUserId:null,reviewedAt:null,
  amounts:[{key:'CALCULATED',label:'Retained expected loss',value:'7.006173',unit:'REPORTING_CURRENCY'}],details:[],blockers:[],links:[],linkCount:0,page:0,hasMore:false,hasCurrentReviewedProcedure:false,reviewBasis:'a'.repeat(64)};
const state={kind:'ECL',evidenceId:id,reviewBasis:view.reviewBasis,canLink:true,canReview:true,decisions:['CHANGES_REQUIRED','REJECTED'],blockers:[],page:0,count:0,hasMore:false,history:[]};
const candidate={resultId,workpaperId:id,procedureId:id,procedureCode:'SYN-01',status:'REVIEWED',revision:1,inputGeneration:1,currentIndependentReview:true,alreadyLinked:false,resultBasis:'d'.repeat(64)};
const preview={kind:'ECL',evidenceId:id,action:'LINK',reviewBasis:view.reviewBasis,requestHash:'e'.repeat(64),canProceed:true,blockers:[]};
const fields={action:'LINK' as const,resultId,resultBasis:candidate.resultBasis,decision:'',reason:'Synthetic human link',evidenceReference:'Synthetic exact evidence'};
function receipt(requestId:string){return {id,requestId,requestHash:preview.requestHash,kind:'ECL',evidenceId:id,action:'LINK',actorId:actor,resultId,linkId:id,decision:'',reason:fields.reason,evidenceReference:fields.evidenceReference,createdAt:'2026-10-03T00:00:00Z'};}

describe('native evidence action review and receipt recovery',()=>{
  let http:HttpTestingController;let params:BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(()=>{sessionStorage.clear();params=new BehaviorSubject(convertToParamMap({id,kind:'ECL'}));TestBed.configureTestingModule({imports:[EvidenceActions],providers:[provideHttpClient(),provideHttpClientTesting(),provideRouter([]),
    {provide:ActivatedRoute,useValue:{paramMap:params}}]});http=TestBed.inject(HttpTestingController);TestBed.inject(SessionService).current.set({userId:actor,firmId:id,generation:'1',staff:true});});
  afterEach(()=>{try{http.verify({ignoreCancelled:true});}finally{TestBed.resetTestingModule();sessionStorage.clear();}});
  async function open(s:object=state){const f=TestBed.createComponent(EvidenceActions);f.detectChanges();TestBed.tick();http.expectOne(root+'?page=0').flush(view);http.expectOne(root+'/actions?page=0').flush(s);
    await f.whenStable();TestBed.tick();http.expectOne(root+'/procedure-results?page=0').flush({kind:'ECL',evidenceId:id,reviewBasis:view.reviewBasis,page:0,count:1,hasMore:false,rows:[candidate]});TestBed.tick();return {f,c:f.componentInstance};}
  async function prepare(c:EvidenceActions){c.model.set({...fields});TestBed.tick();const pending=c.prepare();http.expectOne(root+'/preview').flush({value:preview});await pending;TestBed.tick();}
  it('requires a current preview and new assent, and sends one exact request',async()=>{
    const {c}=await open();await c.execute();http.expectNone(root+'/actions');await prepare(c);await c.execute();http.expectNone(root+'/actions');c.reviewed.set(true);
    const task=c.execute();await c.execute();const request=http.expectOne(root+'/actions');expect(request.request.body.resultBasis).toBe(candidate.resultBasis);expect(request.request.body.reviewed).toBe(true);
    request.flush({value:receipt(request.request.body.requestId)});await task;http.expectOne(root+'?page=0').flush(view);http.expectOne(root+'/actions?page=0').flush(state);
    TestBed.tick();http.expectOne(root+'/procedure-results?page=0').flush({kind:'ECL',evidenceId:id,reviewBasis:view.reviewBasis,page:0,count:1,hasMore:false,rows:[candidate]});
    expect(c.uncertain()).toBe(false);expect(c.preview()).toBeNull();expect(c.reviewed()).toBe(false);
  });
  it('invalidates preview and assent on an edited rationale and saves only bounded editable fields',async()=>{
    const {c}=await open();await prepare(c);c.reviewed.set(true);c.model.update(m=>({...m,reason:'Changed human conclusion'}));TestBed.tick();expect(c.preview()).toBeNull();expect(c.reviewed()).toBe(false);
    expect(c.saveDraft()).toBe(true);expect(sessionStorage.getItem(sessionStorage.key(0)!)).not.toContain('"reviewed"');
    expect(evidenceActionFields({...fields,reviewed:true})).toBeNull();expect(evidenceActionFields({...fields,action:'REVIEW'})).toBeNull();
  });
  it('fences unknown dispatch, verifies an actor-owned receipt, then requires acknowledgment',async()=>{
    const {c}=await open();await prepare(c);c.reviewed.set(true);const task=c.execute();const r=http.expectOne(root+'/actions');const requestId=r.request.body.requestId;
    r.flush({}, {status:503,statusText:'Unavailable'});await task;expect(c.uncertain()).toBe(true);expect(await c.confirmNavigation()).toBe(false);await c.execute();http.expectNone(root+'/actions');
    const recovery=c.reconcile();http.expectOne(root+'/receipts/'+requestId+'?requestHash='+preview.requestHash).flush({found:true,receipt:receipt(requestId)});await recovery;
    expect(c.uncertain()).toBe(true);c.acknowledge();http.expectOne(root+'?page=0').flush(view);http.expectOne(root+'/actions?page=0').flush(state);
    TestBed.tick();http.expectOne(root+'/procedure-results?page=0').flush({kind:'ECL',evidenceId:id,reviewBasis:view.reviewBasis,page:0,count:1,hasMore:false,rows:[candidate]});expect(c.uncertain()).toBe(false);
  });
  it('does not dispatch when the recovery checkpoint cannot be retained',async()=>{
    const {c}=await open();await prepare(c);c.reviewed.set(true);const save=vi.spyOn(Storage.prototype,'setItem').mockImplementation(()=>{throw new Error('Unavailable');});await c.execute();save.mockRestore();
    http.expectNone(root+'/actions');expect(c.message()).toContain('No action was sent');
  });
  it('does not publish a late write receipt after the route changes',async()=>{
    const {c}=await open();await prepare(c);c.reviewed.set(true);const task=c.execute();const r=http.expectOne(root+'/actions');params.next(convertToParamMap({id:resultId,kind:'INVENTORY'}));TestBed.tick();
    r.flush({value:receipt(r.request.body.requestId)});await task;expect(c.receipt()).toBeNull();expect(c.preview()).toBeNull();
    http.expectOne('/api/ui/accounting/evidence/INVENTORY/'+resultId+'?page=0').flush({}, {status:403,statusText:'Denied'});
    http.expectOne('/api/ui/accounting/evidence/INVENTORY/'+resultId+'/actions?page=0').flush({}, {status:403,statusText:'Denied'});
  });
  it('cancels pending reads and clears protected fields when the session ends',async()=>{
    const {f,c}=await open();c.model.set(fields);TestBed.inject(SessionService).clear();TestBed.tick();expect(c.context()).toBeNull();expect(c.model().reason).toBe('');expect(f.nativeElement.textContent).not.toContain('7.006173');
  });
  it('rejects incoherent receipt/history/decision contracts and unsupported target kinds',()=>{
    expect(()=>decodeEvidenceLookup({found:true,receipt:null})).toThrow();expect(()=>decodeEvidenceReceipt({...receipt(id),linkId:null})).toThrow();
    expect(()=>decodeEvidenceActions({...state,kind:'JOURNAL_RISK',decisions:['APPROVED']})).toThrow();expect(()=>decodeEvidenceActions({...state,count:1})).toThrow();
    params.next(convertToParamMap({id,kind:'UNKNOWN'}));const f=TestBed.createComponent(EvidenceActions);f.detectChanges();http.expectNone(x=>x.url.includes('/accounting/evidence/'));
  });
});
